using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Endpoints;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/assets")
            .RequireAuthorization()
            .DisableAntiforgery() // API uses JWT Bearer auth, not cookies with CSRF tokens
            .WithTags("Assets");

        group.MapGet("", GetAssets).RequireAuthorization("RequireAdmin").WithName("GetAssets");
        group.MapGet("all", GetAllAssets).RequireAuthorization("RequireAdmin").WithName("GetAllAssets");
        group.MapGet("{id}", GetAsset).WithName("GetAsset");
        group.MapPost("", UploadAsset).WithName("UploadAsset");
        group.MapPatch("{id}", UpdateAsset).WithName("UpdateAsset");
        group.MapDelete("{id}", DeleteAsset).WithName("DeleteAsset");
        group.MapGet("collection/{collectionId}", GetAssetsByCollection).WithName("GetAssetsByCollection");

        // Multi-collection management
        group.MapGet("{id}/collections", GetAssetCollections).WithName("GetAssetCollections");
        group.MapPost("{id}/collections/{collectionId}", AddAssetToCollection).WithName("AddAssetToCollection");
        group.MapDelete("{id}/collections/{collectionId}", RemoveAssetFromCollection).WithName("RemoveAssetFromCollection");
        group.MapGet("{id}/deletion-context", GetAssetDeletionContext).WithName("GetAssetDeletionContext");

        // Presigned upload flow (for large files — browser uploads directly to MinIO)
        group.MapPost("init-upload", InitUpload).WithName("InitUpload");
        group.MapPost("{id}/confirm-upload", ConfirmUpload).WithName("ConfirmUpload");

        // Rendition/download endpoints
        group.MapGet("{id}/download", DownloadOriginal).WithName("DownloadOriginal");
        group.MapGet("{id}/preview", PreviewOriginal).WithName("PreviewOriginal");
        group.MapGet("{id}/thumb", GetThumbnail).WithName("GetThumbnail");
        group.MapGet("{id}/medium", GetMedium).WithName("GetMedium");
        group.MapGet("{id}/poster", GetPoster).WithName("GetPoster");
    }

    private static async Task<IResult> GetAssets(
        [FromServices] IAssetRepository assetRepository,
        CancellationToken ct,
        int skip = 0,
        int take = 50)
    {
        // In a production system, you'd filter by user's accessible collections
        var assets = await assetRepository.GetByStatusAsync(Asset.StatusReady, skip, take, ct);
        var dtos = assets.Select(a => AssetMapper.ToDto(a)).ToList();
        return Results.Ok(dtos);
    }

    /// <summary>
    /// Get all assets across all accessible collections with search and filter support.
    /// </summary>
    private static async Task<IResult> GetAllAssets(
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] ICollectionRepository collectionRepository,
        [FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct,
        string? query = null,
        string? type = null,
        Guid? collectionId = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        
        // System admins see all assets — skip collection access resolution entirely
        if (isSystemAdmin)
        {
            var adminFilterIds = collectionId.HasValue ? new List<Guid> { collectionId.Value } : null;
            var (adminAssets, adminTotal) = await assetRepository.SearchAllAsync(query, type, sortBy, skip, take, adminFilterIds, ct);
            var adminDtos = adminAssets.Select(a => AssetMapper.ToDto(a, RoleHierarchy.Roles.Admin)).ToList();
            return Results.Ok(new AllAssetsListResponse { Total = adminTotal, Items = adminDtos });
        }

        // Get all collections the user has access to (includes inherited access)
        var accessibleCollections = await collectionRepository.GetAccessibleCollectionsAsync(userId, ct);
        var accessibleCollectionIds = accessibleCollections.Select(c => c.Id).ToList();
        
        // Build role map using the authorization service (respects inheritance)
        var collectionRoles = new Dictionary<Guid, string>();
        foreach (var coll in accessibleCollections)
        {
            var role = await authService.GetUserRoleAsync(userId, coll.Id, ct);
            if (role != null)
                collectionRoles[coll.Id] = role;
        }

        // If a specific collection is requested, filter to just that one (if accessible)
        if (collectionId.HasValue)
        {
            if (!accessibleCollectionIds.Contains(collectionId.Value))
                return Results.Forbid();
            accessibleCollectionIds = new List<Guid> { collectionId.Value };
        }

        // Non-admins are restricted to their accessible collections
        var (assets, total) = await assetRepository.SearchAllAsync(query, type, sortBy, skip, take, accessibleCollectionIds, ct);
        
        // Batch-load collection IDs for all returned assets (single query, no N+1)
        var assetIds = assets.Select(a => a.Id).ToList();
        var assetCollectionMap = await assetCollectionRepo.GetCollectionIdsForAssetsAsync(assetIds, ct);
        
        // For each asset, determine the user's highest role across all collections it belongs to
        var dtos = new List<AssetResponseDto>();
        foreach (var asset in assets)
        {
            var role = RoleHierarchy.Roles.Viewer;
            
            if (assetCollectionMap.TryGetValue(asset.Id, out var assetCollectionIds))
            {
                foreach (var collId in assetCollectionIds)
                {
                    if (collectionRoles.TryGetValue(collId, out var collRole) &&
                        RoleHierarchy.GetLevel(collRole) > RoleHierarchy.GetLevel(role))
                    {
                        role = collRole;
                    }
                }
            }
            
            dtos.Add(AssetMapper.ToDto(asset, role));
        }
        
        return Results.Ok(new AllAssetsListResponse
        {
            Total = total,
            Items = dtos
        });
    }

    private static async Task<IResult> GetAsset(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        
        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // System admins have full access to everything
        if (isSystemAdmin)
            return Results.Ok(AssetMapper.ToDto(asset, RoleHierarchy.Roles.Admin));

        // Check if user has access via any of the asset's collections
        var linkedCollections = await assetCollectionRepo.GetCollectionsForAssetAsync(id, ct);
        foreach (var collection in linkedCollections)
        {
            var role = await authService.GetUserRoleAsync(userId, collection.Id, ct);
            if (role != null)
                return Results.Ok(AssetMapper.ToDto(asset, role));
        }
        
        return Results.Forbid(); // No access to any of the asset's collections
    }

    private static async Task<IResult> GetAssetsByCollection(
        Guid collectionId,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct,
        string? query = null,
        string? type = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        // System admins have full access
        string userRole;
        if (isSystemAdmin)
        {
            userRole = RoleHierarchy.Roles.Admin;
        }
        else
        {
            // Check if user can access this collection and get their role
            var role = await authService.GetUserRoleAsync(userId, collectionId, ct);
            if (role == null)
                return Results.Forbid();
            userRole = role;
        }

        var (assets, total) = await assetRepository.SearchAsync(collectionId, query, type, sortBy, skip, take, ct);
        var dtos = assets.Select(a => AssetMapper.ToDto(a, userRole)).ToList();
        return Results.Ok(new AssetListResponse
        {
            CollectionId = collectionId,
            Total = total,
            Items = dtos
        });
    }

    private static async Task<IResult> UploadAsset(
        IFormFile file,
        [FromForm] Guid collectionId,
        [FromForm] string title,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        [FromServices] IMediaProcessingService mediaProcessingService,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IAuditService audit,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var bucketName = StorageConfig.GetBucketName(configuration);

        // Check if user can contribute to this collection
        var canContribute = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
        if (!canContribute)
            return Results.Forbid();

        if (file == null || file.Length == 0)
            return Results.BadRequest(ApiError.BadRequest("File is required"));

        // Enforce MaxUploadSizeMb
        var sizeError = ValidateFileSize(file.Length, configuration);
        if (sizeError != null) return sizeError;

        // Create asset entity
        var asset = CreateAssetEntity(file.FileName, file.ContentType, file.Length, userId, Asset.StatusProcessing);
        if (!string.IsNullOrEmpty(title))
            asset.Title = title;

        // Upload to MinIO
        using var stream = file.OpenReadStream();
        await minioAdapter.UploadAsync(bucketName, asset.OriginalObjectKey, stream, file.ContentType, ct);

        // Save asset to database
        await assetRepository.CreateAsync(asset, ct);
        
        // Add asset to the collection via join table
        await assetCollectionRepo.AddToCollectionAsync(asset.Id, collectionId, userId, ct);

        await audit.LogAsync("asset.created", "asset", asset.Id, userId,
            new() { ["title"] = title ?? "", ["collectionId"] = collectionId, ["contentType"] = file.ContentType }, httpContext, ct);

        // Schedule processing job
        var jobId = await mediaProcessingService.ScheduleProcessingAsync(asset.Id, asset.AssetType, asset.OriginalObjectKey, ct);

        return Results.Accepted($"/api/assets/{asset.Id}", new AssetUploadResult
        {
            Id = asset.Id,
            Status = Asset.StatusProcessing,
            JobId = jobId,
            Message = "Asset uploaded. Processing in progress."
        });
    }

    private static async Task<IResult> UpdateAsset(
        Guid id,
        UpdateAssetDto dto,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IAuditService audit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization - require contributor on any of the asset's collections
        if (!await CanAccessAssetAsync(id, userId, isSystemAdmin, assetCollectionRepo, authService, ct,
                requiredRole: RoleHierarchy.Roles.Contributor))
            return Results.Forbid();

        // Only update fields that are provided
        if (dto.Title != null)
            asset.Title = dto.Title;
        if (dto.Description != null)
            asset.Description = dto.Description;
        if (dto.Tags != null)
            asset.Tags = dto.Tags;
        if (dto.MetadataJson != null)
            asset.MetadataJson = dto.MetadataJson;
        asset.UpdatedAt = DateTime.UtcNow;

        await assetRepository.UpdateAsync(asset, ct);

        await audit.LogAsync("asset.updated", "asset", id, userId, httpContext: httpContext, ct: ct);

        return Results.Ok(AssetMapper.ToDto(asset));
    }

    private static async Task<IResult> DeleteAsset(
        Guid id,
        [FromQuery] Guid? fromCollectionId,
        [FromQuery] bool permanent,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IAssetDeletionService deletionService,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IAuditService audit,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = StorageConfig.GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        var assetCollections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);

        // If fromCollectionId is set and permanent is false: "Remove from this collection" mode
        if (fromCollectionId.HasValue && !permanent && assetCollections.Count > 1)
        {
            // Require contributor+ on the source collection
            if (!isSystemAdmin)
            {
                var canAccess = await authService.CheckAccessAsync(userId, fromCollectionId.Value, RoleHierarchy.Roles.Contributor, ct);
                if (!canAccess)
                    return Results.Forbid();
            }

            await assetCollectionRepo.RemoveFromCollectionAsync(id, fromCollectionId.Value, ct);

            await audit.LogAsync("asset.removed_from_collection", "asset", id, userId,
                new() { ["title"] = asset.Title, ["collectionId"] = fromCollectionId.Value.ToString() }, httpContext, ct);

            return Results.NoContent();
        }

        // Permanent delete mode — check authorization on the asset's collections
        if (!isSystemAdmin)
        {
            var authorizedCollectionIds = await authService.FilterAccessibleAsync(
                userId, assetCollections, RoleHierarchy.Roles.Manager, ct);

            if (authorizedCollectionIds.Count == 0)
                return Results.Forbid();

            var unauthorizedRemain = assetCollections.Except(authorizedCollectionIds).Any();

            if (unauthorizedRemain)
            {
                // User can't fully delete — just remove from authorized collections
                foreach (var collId in authorizedCollectionIds)
                    await assetCollectionRepo.RemoveFromCollectionAsync(id, collId, ct);

                await audit.LogAsync("asset.removed_from_collections", "asset", id, userId,
                    new() { ["title"] = asset.Title, ["count"] = authorizedCollectionIds.Count.ToString() }, httpContext, ct);

                return Results.NoContent();
            }
        }

        // Full permanent delete — user has authority over all collections (or is admin)
        await deletionService.PermanentDeleteAsync(asset, bucketName, ct);

        await audit.LogAsync("asset.deleted", "asset", id, userId,
            new() { ["title"] = asset.Title }, httpContext, ct);

        return Results.NoContent();
    }

    /// <summary>
    /// Returns context information needed by the UI to decide which deletion
    /// options to present (remove from collection vs permanent delete).
    /// </summary>
    private static async Task<IResult> GetAssetDeletionContext(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        var collectionIds = await assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);

        bool canDeleteAll = isSystemAdmin;
        if (!isSystemAdmin)
        {
            var accessible = await authService.FilterAccessibleAsync(userId, collectionIds, RoleHierarchy.Roles.Manager, ct);
            canDeleteAll = accessible.Count == collectionIds.Count;
        }

        return Results.Ok(new AssetDeletionContextDto
        {
            CollectionCount = collectionIds.Count,
            CanDeletePermanently = canDeleteAll
        });
    }

    #region Multi-Collection Endpoints

    /// <summary>
    /// Get all collections an asset belongs to (primary + linked).
    /// </summary>
    private static async Task<IResult> GetAssetCollections(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check if user can access via any of the asset's collections (system admins bypass)
        if (!await CanAccessAssetAsync(id, userId, isSystemAdmin, assetCollectionRepo, authService, ct))
            return Results.Forbid();

        var collections = await assetCollectionRepo.GetCollectionsForAssetAsync(id, ct);

        var result = collections.Select(c => new AssetCollectionDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description
        });

        return Results.Ok(result);
    }

    /// <summary>
    /// Add an asset to an additional collection.
    /// </summary>
    private static async Task<IResult> AddAssetToCollection(
        Guid id,
        Guid collectionId,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound(ApiError.NotFound("Asset not found"));

        // System admins bypass permission checks
        if (!isSystemAdmin)
        {
            // Check if user can contribute to any of the asset's collections
            var assetCollections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);
            bool canAccessAsset = false;
            
            foreach (var assetCollectionId in assetCollections)
            {
                if (await authService.CheckAccessAsync(userId, assetCollectionId, RoleHierarchy.Roles.Contributor, ct))
                {
                    canAccessAsset = true;
                    break;
                }
            }
            
            // If asset has no collections yet, allow adding to first collection if user has access
            if (!canAccessAsset && assetCollections.Count == 0)
            {
                canAccessAsset = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
            }
            
            if (!canAccessAsset)
                return Results.Json(ApiError.Forbidden("You don't have permission to manage this asset"), statusCode: 403);

            var canAccessTarget = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
            if (!canAccessTarget)
                return Results.Json(ApiError.Forbidden("You don't have permission to add assets to this collection"), statusCode: 403);
        }

        var result = await assetCollectionRepo.AddToCollectionAsync(id, collectionId, userId, ct);
        if (result == null)
            return Results.BadRequest(ApiError.BadRequest("Asset is already linked to this collection or collection not found"));

        return Results.Created($"/api/assets/{id}/collections/{collectionId}", new AssetAddedToCollectionResponse
        {
            AssetId = id,
            CollectionId = collectionId,
            AddedAt = result.AddedAt,
            Message = "Asset added to collection"
        });
    }

    /// <summary>
    /// Remove an asset from a collection. If the asset becomes orphaned (0 collections), it is auto-deleted.
    /// </summary>
    private static async Task<IResult> RemoveAssetFromCollection(
        Guid id,
        Guid collectionId,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetDeletionService deletionService,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IAuditService audit,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound(ApiError.NotFound("Asset not found"));

        if (!isSystemAdmin)
        {
            var canAccess = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
            if (!canAccess)
                return Results.Json(ApiError.Forbidden("You don't have permission to manage this asset in this collection"), statusCode: 403);
        }

        var bucketName = StorageConfig.GetBucketName(configuration);
        var (removed, permanentlyDeleted) = await deletionService.RemoveFromCollectionAsync(asset, collectionId, bucketName, ct);
        if (!removed)
            return Results.NotFound(ApiError.NotFound("Asset is not linked to this collection"));

        if (permanentlyDeleted)
        {
            await audit.LogAsync("asset.deleted", "asset", id, userId,
                new() { ["title"] = asset.Title, ["reason"] = "orphaned" }, httpContext, ct);
        }
        else
        {
            await audit.LogAsync("asset.removed_from_collection", "asset", id, userId,
                new() { ["title"] = asset.Title, ["collectionId"] = collectionId.ToString() }, httpContext, ct);
        }

        return Results.NoContent();
    }

    #endregion

    #region Presigned Upload Endpoints

    /// <summary>
    /// Step 1 of presigned upload: Creates an asset record, generates a presigned PUT URL,
    /// and returns it to the client. The client then uploads the file directly to MinIO.
    /// This avoids the file passing through the API server and SignalR circuit,
    /// which is critical for large files (up to 700MB video).
    /// </summary>
    private static async Task<IResult> InitUpload(
        InitUploadRequest request,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var bucketName = StorageConfig.GetBucketName(configuration);

        // Enforce MaxUploadSizeMb
        var sizeError = ValidateFileSize(request.FileSize, configuration);
        if (sizeError != null) return sizeError;

        // Check contribution permission
        var canContribute = await authService.CheckAccessAsync(userId, request.CollectionId, RoleHierarchy.Roles.Contributor, ct);
        if (!canContribute)
            return Results.Forbid();

        // Create asset record in "uploading" status
        var asset = CreateAssetEntity(request.FileName, request.ContentType, request.FileSize, userId, Asset.StatusUploading);
        if (!string.IsNullOrEmpty(request.Title))
            asset.Title = request.Title;

        await assetRepository.CreateAsync(asset, ct);
        await assetCollectionRepo.AddToCollectionAsync(asset.Id, request.CollectionId, userId, ct);

        // Generate presigned PUT URL (15 minute expiry for large uploads)
        var presignedUrl = await minioAdapter.GetPresignedUploadUrlAsync(bucketName, asset.OriginalObjectKey, expirySeconds: Constants.Limits.PresignedUploadExpirySec, ct);

        return Results.Ok(new InitUploadResponse
        {
            AssetId = asset.Id,
            ObjectKey = asset.OriginalObjectKey,
            UploadUrl = presignedUrl,
            ExpiresInSeconds = Constants.Limits.PresignedUploadExpirySec
        });
    }

    /// <summary>
    /// Step 2 of presigned upload: Client calls this after uploading the file to MinIO.
    /// Verifies the object exists and matches expected size, then schedules processing.
    /// </summary>
    private static async Task<IResult> ConfirmUpload(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IMinIOAdapter minioAdapter,
        [FromServices] IMediaProcessingService mediaProcessingService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var bucketName = StorageConfig.GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Only the uploader can confirm
        if (asset.CreatedByUserId != userId)
            return Results.Forbid();

        // Must be in "uploading" status
        if (asset.Status != Asset.StatusUploading)
            return Results.BadRequest(ApiError.BadRequest("Asset is not in uploading state"));

        // Verify the object actually exists in MinIO and check size
        var stat = await minioAdapter.StatObjectAsync(bucketName, asset.OriginalObjectKey, ct);
        if (stat == null)
            return Results.BadRequest(ApiError.BadRequest("File not found in storage. Upload may have failed or expired."));

        // Update asset with actual size from MinIO and set to processing
        asset.SizeBytes = stat.Size;
        asset.Status = Asset.StatusProcessing;
        asset.UpdatedAt = DateTime.UtcNow;
        await assetRepository.UpdateAsync(asset, ct);

        // Schedule media processing
        var jobId = await mediaProcessingService.ScheduleProcessingAsync(asset.Id, asset.AssetType, asset.OriginalObjectKey, ct);

        return Results.Ok(new AssetUploadResult
        {
            Id = asset.Id,
            Status = Asset.StatusProcessing,
            SizeBytes = stat.Size,
            JobId = jobId,
            Message = "Upload confirmed. Processing in progress."
        });
    }

    #endregion

    #region Rendition Endpoints

    private static async Task<IResult> DownloadOriginal(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (asset, error) = await LoadAuthorizedAssetAsync(id, httpContext, assetRepository, assetCollectionRepo, authService, ct);
        if (error != null) return error;
        return await ServeRenditionAsync(asset!, "original", minioAdapter, configuration, ct);
    }

    /// <summary>
    /// Preview endpoint — redirects to a presigned URL for inline browser display.
    /// Supports PDF, images, video, and other browser-viewable content types.
    /// </summary>
    private static async Task<IResult> PreviewOriginal(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (asset, error) = await LoadAuthorizedAssetAsync(id, httpContext, assetRepository, assetCollectionRepo, authService, ct);
        if (error != null) return error;
        return await ServeRenditionAsync(asset!, "original", minioAdapter, configuration, ct);
    }

    private static async Task<IResult> GetThumbnail(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (asset, error) = await LoadAuthorizedAssetAsync(id, httpContext, assetRepository, assetCollectionRepo, authService, ct);
        if (error != null) return error;
        return await ServeRenditionAsync(asset!, "thumb", minioAdapter, configuration, ct);
    }

    private static async Task<IResult> GetMedium(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (asset, error) = await LoadAuthorizedAssetAsync(id, httpContext, assetRepository, assetCollectionRepo, authService, ct);
        if (error != null) return error;
        return await ServeRenditionAsync(asset!, "medium", minioAdapter, configuration, ct);
    }

    private static async Task<IResult> GetPoster(
        Guid id,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (asset, error) = await LoadAuthorizedAssetAsync(id, httpContext, assetRepository, assetCollectionRepo, authService, ct);
        if (error != null) return error;
        return await ServeRenditionAsync(asset!, "poster", minioAdapter, configuration, ct);
    }

    #endregion

    #region Helper Methods
    
    /// <summary>
    /// Loads an asset by ID and verifies the requesting user has access.
    /// Returns either the authorized asset or an IResult error.
    /// </summary>
    private static async Task<(Asset? asset, IResult? error)> LoadAuthorizedAssetAsync(
        Guid id,
        HttpContext httpContext,
        IAssetRepository assetRepository,
        IAssetCollectionRepository assetCollectionRepo,
        ICollectionAuthorizationService authService,
        CancellationToken ct,
        string requiredRole = RoleHierarchy.Roles.Viewer)
    {
        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return (null, Results.NotFound());

        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        if (!await CanAccessAssetAsync(id, userId, isSystemAdmin, assetCollectionRepo, authService, ct, requiredRole))
            return (null, Results.Forbid());

        return (asset, null);
    }

    /// <summary>
    /// Resolves the appropriate object key for the requested rendition size and redirects via presigned URL.
    /// </summary>
    private static async Task<IResult> ServeRenditionAsync(
        Asset asset,
        string size,
        IMinIOAdapter minioAdapter,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var bucketName = StorageConfig.GetBucketName(configuration);

        string? objectKey = size.ToLower() switch
        {
            "original" => asset.OriginalObjectKey,
            "thumb" => asset.ThumbObjectKey,
            "medium" => !string.IsNullOrEmpty(asset.MediumObjectKey) ? asset.MediumObjectKey : asset.OriginalObjectKey,
            "poster" => asset.PosterObjectKey,
            _ => asset.OriginalObjectKey
        };

        if (string.IsNullOrEmpty(objectKey))
            return Results.NotFound($"{size} rendition not available");

        var presignedUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, objectKey, expirySeconds: Constants.Limits.PresignedDownloadExpirySec, ct);
        return Results.Redirect(presignedUrl);
    }

    /// <summary>
    /// Check if a user can access an asset by checking all collections it belongs to.
    /// Uses batch auth queries to avoid N+1 database round-trips.
    /// </summary>
    private static async Task<bool> CanAccessAssetAsync(
        Guid assetId,
        string userId,
        bool isSystemAdmin,
        IAssetCollectionRepository assetCollectionRepo,
        ICollectionAuthorizationService authService,
        CancellationToken ct = default,
        string requiredRole = RoleHierarchy.Roles.Viewer)
    {
        if (isSystemAdmin)
            return true;

        var collections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        var accessible = await authService.FilterAccessibleAsync(userId, collections, requiredRole, ct);
        return accessible.Count > 0;
    }



    /// <summary>
    /// Validates file size against the configured maximum upload size.
    /// Returns an error result if the size exceeds the limit, null otherwise.
    /// </summary>
    private static IResult? ValidateFileSize(long fileSize, IConfiguration configuration)
    {
        var maxSizeMb = configuration.GetValue("App:MaxUploadSizeMb", Constants.Limits.DefaultMaxUploadSizeMb);
        var maxSizeBytes = (long)maxSizeMb * 1024 * 1024;
        return fileSize > maxSizeBytes
            ? Results.BadRequest(ApiError.BadRequest($"File size exceeds the maximum allowed size of {maxSizeMb} MB"))
            : null;
    }

    /// <summary>
    /// Creates a new Asset entity with common properties pre-set.
    /// </summary>
    private static Asset CreateAssetEntity(
        string fileName, string contentType, long sizeBytes, string userId, string status)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        var assetType = AssetTypeHelper.DetermineAssetType(contentType, extension);
        var assetId = Guid.NewGuid();
        var objectKey = $"originals/{assetId}-{Path.GetFileName(fileName)}";

        return new Asset
        {
            Id = assetId,
            AssetType = assetType,
            Status = status,
            Title = Path.GetFileNameWithoutExtension(fileName),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            OriginalObjectKey = objectKey,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            UpdatedAt = DateTime.UtcNow
        };
    }



    #endregion
}
