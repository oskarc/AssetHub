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

        group.MapGet("", GetAssets).WithName("GetAssets");
        group.MapGet("all", GetAllAssets).WithName("GetAllAssets");
        group.MapGet("{id}", GetAsset).WithName("GetAsset");
        group.MapPost("", UploadAsset).WithName("UploadAsset");
        group.MapPatch("{id}", UpdateAsset).WithName("UpdateAsset");
        group.MapDelete("{id}", DeleteAsset).WithName("DeleteAsset");
        group.MapGet("collection/{collectionId}", GetAssetsByCollection).WithName("GetAssetsByCollection");

        // Multi-collection management
        group.MapGet("{id}/collections", GetAssetCollections).WithName("GetAssetCollections");
        group.MapPost("{id}/collections/{collectionId}", AddAssetToCollection).WithName("AddAssetToCollection");
        group.MapDelete("{id}/collections/{collectionId}", RemoveAssetFromCollection).WithName("RemoveAssetFromCollection");

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
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAclRepository aclRepository,
        HttpContext httpContext,
        CancellationToken ct,
        string? query = null,
        string? type = null,
        Guid? collectionId = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        
        // Get all collections the user has access to
        var accessibleCollections = await collectionRepository.GetAccessibleCollectionsAsync(userId, ct);
        var accessibleCollectionIds = accessibleCollections.Select(c => c.Id).ToList();
        
        // Get user's role for each collection from ACLs
        var userAcls = await aclRepository.GetByUserAsync(userId, ct);
        var collectionRoles = userAcls.ToDictionary(a => a.CollectionId, a => a.Role);

        // If a specific collection is requested, filter to just that one (if accessible)
        if (collectionId.HasValue)
        {
            if (!accessibleCollectionIds.Contains(collectionId.Value))
                return Results.Forbid();
            accessibleCollectionIds = new List<Guid> { collectionId.Value };
        }

        var (assets, total) = await assetRepository.SearchAllAsync(query, type, sortBy, skip, take, ct);
        
        // For each asset, determine the user's highest role across all collections it belongs to
        var dtos = new List<AssetResponseDto>();
        foreach (var asset in assets)
        {
            // Get the collections this asset belongs to
            var assetCollIds = await assetCollectionRepo.GetCollectionsForAssetAsync(asset.Id, ct);
            var assetCollectionIds = assetCollIds.Select(c => c.Id).ToList();
            
            // Find the highest role the user has across those collections
            var role = RoleHierarchy.Roles.Viewer; // default
            foreach (var collId in assetCollectionIds)
            {
                if (collectionRoles.TryGetValue(collId, out var collRole))
                {
                    // If the current role is higher than what we have, use it
                    if (RoleHierarchy.GetLevel(collRole) > RoleHierarchy.GetLevel(role))
                        role = collRole;
                }
            }
            
            dtos.Add(AssetMapper.ToDto(asset, role));
        }
        
        return Results.Ok(new
        {
            total,
            items = dtos
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
        var userId = httpContext.User.GetUserIdOrDefault();
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
        var userId = httpContext.User.GetUserIdOrDefault();
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
        return Results.Ok(new
        {
            collectionId,
            total,
            items = dtos
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
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var bucketName = StorageConfig.GetBucketName(configuration);

        // Check if user can contribute to this collection
        var canContribute = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
        if (!canContribute)
            return Results.Forbid();

        if (file == null || file.Length == 0)
            return Results.BadRequest("File is required");

        try
        {
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

            // Schedule processing job
            var jobId = await mediaProcessingService.ScheduleProcessingAsync(assetId, assetType, objectKey, ct);

            return Results.Accepted($"/api/assets/{assetId}", new
            {
                id = assetId,
                status = Asset.StatusProcessing,
                jobId,
                message = "Asset uploaded. Processing in progress."
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Upload failed: {ex.Message}");
        }
    }

    private static async Task<IResult> UpdateAsset(
        Guid id,
        UpdateAssetDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
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
        return Results.Ok(AssetMapper.ToDto(asset));
    }

    private static async Task<IResult> DeleteAsset(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetCollectionRepository assetCollectionRepo,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
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

        try
        {
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

            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Deletion failed: {ex.Message}");
        }
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
        var userId = httpContext.User.GetUserIdOrDefault();
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
        var userId = httpContext.User.GetUserIdOrDefault();
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

        return Results.Created($"/api/assets/{id}/collections/{collectionId}", new
        {
            assetId = id,
            collectionId,
            addedAt = result.AddedAt,
            message = "Asset added to collection"
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
        var userId = httpContext.User.GetUserIdOrDefault();
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
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = StorageConfig.GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization via any of the asset's collections
        if (!await CanAccessAssetAsync(id, userId, isSystemAdmin, assetCollectionRepo, authService, ct))
            return Results.Forbid();

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.OriginalObjectKey, ct);
            var fileName = asset.Title + Path.GetExtension(asset.OriginalObjectKey);
            return Results.File(stream, asset.ContentType, fileName);
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Download failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Preview endpoint - returns file with inline disposition (for browser display, not download).
    /// Supports PDF, images, and other browser-viewable content types.
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
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = StorageConfig.GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        if (!await CanAccessAssetAsync(id, userId, isSystemAdmin, assetCollectionRepo, authService, ct))
            return Results.Forbid();

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.OriginalObjectKey, ct);
            // Return without filename to trigger inline display (not attachment download)
            return Results.File(stream, asset.ContentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Preview failed: {ex.Message}");
        }
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
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = StorageConfig.GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        if (!await CanAccessAssetAsync(id, userId, isSystemAdmin, assetCollectionRepo, authService, ct))
            return Results.Forbid();

        if (string.IsNullOrEmpty(asset.ThumbObjectKey))
            return Results.NotFound("Thumbnail not available");

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.ThumbObjectKey, ct);
            return Results.File(stream, "image/webp");
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Failed to get thumbnail: {ex.Message}");
        }
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
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = StorageConfig.GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        if (!await CanAccessAssetAsync(id, userId, isSystemAdmin, assetCollectionRepo, authService, ct))
            return Results.Forbid();

        if (string.IsNullOrEmpty(asset.MediumObjectKey))
        {
            // Fall back to original if medium not available
            try
            {
                var stream = await minioAdapter.DownloadAsync(bucketName, asset.OriginalObjectKey, ct);
                return Results.File(stream, asset.ContentType);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Failed to get asset: {ex.Message}");
            }
        }

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.MediumObjectKey, ct);
            return Results.File(stream, asset.ContentType);
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Failed to get medium rendition: {ex.Message}");
        }
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
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = StorageConfig.GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        if (!await CanAccessAssetAsync(id, userId, isSystemAdmin, assetCollectionRepo, authService, ct))
            return Results.Forbid();

        if (string.IsNullOrEmpty(asset.PosterObjectKey))
            return Results.NotFound("Poster not available");

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.PosterObjectKey, ct);
            return Results.File(stream, "image/webp");
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Failed to get poster: {ex.Message}");
        }
    }

    #endregion

    #region Helper Methods
    
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
