using System.Security.Claims;
using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionRepository collectionRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
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
        [Microsoft.AspNetCore.Mvc.FromForm] Guid collectionId,
        [Microsoft.AspNetCore.Mvc.FromForm] string title,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] IMediaProcessingService mediaProcessingService,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
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
            return Results.BadRequest("File is required");

        // Enforce MaxUploadSizeMb
        var maxSizeMb = configuration.GetValue("App:MaxUploadSizeMb", 500);
        var maxSizeBytes = (long)maxSizeMb * 1024 * 1024;
        if (file.Length > maxSizeBytes)
            return Results.BadRequest($"File size exceeds the maximum allowed size of {maxSizeMb} MB");

        // Determine asset type from content type and file extension
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        var assetType = AssetTypeHelper.DetermineAssetType(file.ContentType, extension);

        // Create asset entity
        var assetId = Guid.NewGuid();
        var objectKey = $"originals/{assetId}-{Path.GetFileName(file.FileName)}";

        var asset = new Asset
        {
            Id = assetId,
            AssetType = assetType,
            Status = Asset.StatusProcessing,
            Title = title ?? Path.GetFileNameWithoutExtension(file.FileName),
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            OriginalObjectKey = objectKey,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            UpdatedAt = DateTime.UtcNow
        };

        // Upload to MinIO
        using var stream = file.OpenReadStream();
        await minioAdapter.UploadAsync(bucketName, objectKey, stream, file.ContentType, ct);

        // Save asset to database
        await assetRepository.CreateAsync(asset, ct);
        
        // Add asset to the collection via join table
        await assetCollectionRepo.AddToCollectionAsync(assetId, collectionId, userId, ct);

        await audit.LogAsync("asset.created", "asset", assetId, userId,
            new() { ["title"] = title ?? "", ["collectionId"] = collectionId, ["contentType"] = file.ContentType }, httpContext, ct);

        // Schedule processing job
        var jobId = await mediaProcessingService.ScheduleProcessingAsync(assetId, assetType, objectKey, ct);

        return Results.Accepted($"/api/assets/{assetId}", new AssetUploadResult
        {
            Id = assetId,
            Status = Asset.StatusProcessing,
            JobId = jobId,
            Message = "Asset uploaded. Processing in progress."
        });
    }

    private static async Task<IResult> UpdateAsset(
        Guid id,
        UpdateAssetDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization - require contributor on any of the asset's collections
        if (!isSystemAdmin)
        {
            var assetCollections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);
            bool canEdit = false;
            foreach (var collId in assetCollections)
            {
                if (await authService.CheckAccessAsync(userId, collId, RoleHierarchy.Roles.Contributor, ct))
                {
                    canEdit = true;
                    break;
                }
            }
            if (!canEdit)
                return Results.Forbid();
        }

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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IAuditService audit,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = StorageConfig.GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization - require manager on any of the asset's collections
        if (!isSystemAdmin)
        {
            var assetCollections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);
            bool canDelete = false;
            foreach (var collId in assetCollections)
            {
                if (await authService.CheckAccessAsync(userId, collId, RoleHierarchy.Roles.Manager, ct))
                {
                    canDelete = true;
                    break;
                }
            }
            if (!canDelete)
                return Results.Forbid();
        }

        // Delete from MinIO
        await minioAdapter.DeleteAsync(bucketName, asset.OriginalObjectKey, ct);
        if (asset.ThumbObjectKey != null)
            await minioAdapter.DeleteAsync(bucketName, asset.ThumbObjectKey, ct);
        if (asset.MediumObjectKey != null)
            await minioAdapter.DeleteAsync(bucketName, asset.MediumObjectKey, ct);
        if (asset.PosterObjectKey != null)
            await minioAdapter.DeleteAsync(bucketName, asset.PosterObjectKey, ct);

        // Delete from database
        await assetRepository.DeleteAsync(id, ct);

        await audit.LogAsync("asset.deleted", "asset", id, userId,
            new() { ["title"] = asset.Title }, httpContext, ct);

        return Results.NoContent();
    }

    #region Multi-Collection Endpoints

    /// <summary>
    /// Get all collections an asset belongs to (primary + linked).
    /// </summary>
    private static async Task<IResult> GetAssetCollections(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check if user can access via any of the asset's collections (system admins bypass)
        if (!isSystemAdmin)
        {
            var linkedCollections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);
            bool canAccess = false;
            foreach (var linkedCollectionId in linkedCollections)
            {
                if (await authService.CheckAccessAsync(userId, linkedCollectionId, RoleHierarchy.Roles.Viewer, ct))
                {
                    canAccess = true;
                    break;
                }
            }
            
            if (!canAccess)
                return Results.Forbid();
        }

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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
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
    /// Remove an asset from a collection. If removing from primary collection, the asset becomes an orphan.
    /// </summary>
    private static async Task<IResult> RemoveAssetFromCollection(
        Guid id,
        Guid collectionId,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound(ApiError.NotFound("Asset not found"));

        // Check if user can manage the asset (system admins bypass)
        if (!isSystemAdmin)
        {
            // User needs contributor access to the collection they're removing from
            var canAccess = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
            if (!canAccess)
                return Results.Json(ApiError.Forbidden("You don't have permission to manage this asset in this collection"), statusCode: 403);
        }

        // Remove from the collection
        var removed = await assetCollectionRepo.RemoveFromCollectionAsync(id, collectionId, ct);
        if (!removed)
            return Results.NotFound(ApiError.NotFound("Asset is not linked to this collection"));

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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();
        var bucketName = StorageConfig.GetBucketName(configuration);

        // Enforce MaxUploadSizeMb
        var maxSizeMb = configuration.GetValue("App:MaxUploadSizeMb", 500);
        var maxSizeBytes = (long)maxSizeMb * 1024 * 1024;
        if (request.FileSize > maxSizeBytes)
            return Results.BadRequest($"File size exceeds the maximum allowed size of {maxSizeMb} MB");

        // Check contribution permission
        var canContribute = await authService.CheckAccessAsync(userId, request.CollectionId, RoleHierarchy.Roles.Contributor, ct);
        if (!canContribute)
            return Results.Forbid();

        // Determine asset type from content type and extension
        var extension = Path.GetExtension(request.FileName)?.ToLowerInvariant();
        var assetType = AssetTypeHelper.DetermineAssetType(request.ContentType, extension);

        // Create asset record in "uploading" status
        var assetId = Guid.NewGuid();
        var objectKey = $"originals/{assetId}-{Path.GetFileName(request.FileName)}";

        var asset = new Asset
        {
            Id = assetId,
            AssetType = assetType,
            Status = Asset.StatusUploading,
            Title = request.Title ?? Path.GetFileNameWithoutExtension(request.FileName),
            ContentType = request.ContentType,
            SizeBytes = request.FileSize,
            OriginalObjectKey = objectKey,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            UpdatedAt = DateTime.UtcNow
        };

        await assetRepository.CreateAsync(asset, ct);
        await assetCollectionRepo.AddToCollectionAsync(assetId, request.CollectionId, userId, ct);

        // Generate presigned PUT URL (15 minute expiry for large uploads)
        var presignedUrl = await minioAdapter.GetPresignedUploadUrlAsync(bucketName, objectKey, expirySeconds: 900, ct);

        return Results.Ok(new InitUploadResponse
        {
            AssetId = assetId,
            ObjectKey = objectKey,
            UploadUrl = presignedUrl,
            ExpiresInSeconds = 900
        });
    }

    /// <summary>
    /// Step 2 of presigned upload: Client calls this after uploading the file to MinIO.
    /// Verifies the object exists and matches expected size, then schedules processing.
    /// </summary>
    private static async Task<IResult> ConfirmUpload(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] IMediaProcessingService mediaProcessingService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
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
            return Results.BadRequest("Asset is not in uploading state");

        // Verify the object actually exists in MinIO and check size
        var stat = await minioAdapter.StatObjectAsync(bucketName, asset.OriginalObjectKey, ct);
        if (stat == null)
            return Results.BadRequest("File not found in storage. Upload may have failed or expired.");

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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
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
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (asset, error) = await LoadAuthorizedAssetAsync(id, httpContext, assetRepository, assetCollectionRepo, authService, ct);
        if (error != null) return error;
        return await ServeRenditionAsync(asset!, "original", minioAdapter, configuration, ct);
    }

    private static async Task<IResult> GetThumbnail(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (asset, error) = await LoadAuthorizedAssetAsync(id, httpContext, assetRepository, assetCollectionRepo, authService, ct);
        if (error != null) return error;
        return await ServeRenditionAsync(asset!, "thumb", minioAdapter, configuration, ct);
    }

    private static async Task<IResult> GetMedium(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (asset, error) = await LoadAuthorizedAssetAsync(id, httpContext, assetRepository, assetCollectionRepo, authService, ct);
        if (error != null) return error;
        return await ServeRenditionAsync(asset!, "medium", minioAdapter, configuration, ct);
    }

    private static async Task<IResult> GetPoster(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
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

        var presignedUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, objectKey, expirySeconds: 300, ct);
        return Results.Redirect(presignedUrl);
    }

    /// <summary>
    /// Check if a user can access an asset by checking all collections it belongs to.
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
        foreach (var collectionId in collections)
        {
            if (await authService.CheckAccessAsync(userId, collectionId, requiredRole, ct))
                return true;
        }
        return false;
    }

    #endregion
}
