using System.Security.Claims;
using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace AssetHub.Endpoints;

public static class AssetEndpoints
{
    private static string GetBucketName(IConfiguration configuration) =>
        configuration["MinIO:BucketName"] ?? "assethub-dev";

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
        var dtos = assets.Select(a => MapToDto(a)).ToList();
        return Results.Ok(dtos);
    }

    /// <summary>
    /// Get all assets across all accessible collections with search and filter support.
    /// </summary>
    private static async Task<IResult> GetAllAssets(
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
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
        var dtos = assets.Select(a => MapToDto(a, a.CollectionId.HasValue ? collectionRoles.GetValueOrDefault(a.CollectionId.Value, RoleHierarchy.Roles.Viewer) : RoleHierarchy.Roles.Viewer)).ToList();
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
            return Results.Ok(MapToDto(asset, RoleHierarchy.Roles.Admin));

        // For orphan assets, check if user has access via any linked collection
        if (asset.CollectionId == null)
        {
            var linkedCollections = await assetCollectionRepo.GetCollectionsForAssetAsync(id, ct);
            foreach (var collection in linkedCollections)
            {
                var role = await authService.GetUserRoleAsync(userId, collection.Id);
                if (role != null)
                    return Results.Ok(MapToDto(asset, role));
            }
            return Results.Forbid(); // No access to any linked collection
        }

        // Get user's role on the asset's primary collection
        var userRole = await authService.GetUserRoleAsync(userId, asset.CollectionId.Value);
        if (userRole == null)
            return Results.Forbid(); // User has no access to this asset's collection

        return Results.Ok(MapToDto(asset, userRole));
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
            var role = await authService.GetUserRoleAsync(userId, collectionId);
            if (role == null)
                return Results.Forbid();
            userRole = role;
        }

        var (assets, total) = await assetRepository.SearchAsync(collectionId, query, type, sortBy, skip, take, ct);
        var dtos = assets.Select(a => MapToDto(a, userRole)).ToList();
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
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] IMediaProcessingService mediaProcessingService,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var bucketName = GetBucketName(configuration);

        // Check if user can contribute to this collection
        var canContribute = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor);
        if (!canContribute)
            return Results.Forbid();

        if (file == null || file.Length == 0)
            return Results.BadRequest("File is required");

        try
        {
            // Determine asset type from content type and file extension
            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            var assetType = DetermineAssetType(file.ContentType, extension);

            // Create asset entity
            var assetId = Guid.NewGuid();
            var objectKey = $"originals/{assetId}-{Path.GetFileName(file.FileName)}";

            var asset = new Asset
            {
                Id = assetId,
                CollectionId = collectionId,
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

            // Schedule processing job
            var jobId = await mediaProcessingService.ScheduleProcessingAsync(assetId, assetType, objectKey);

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
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization (orphan assets require admin, or contributor on primary collection)
        if (!isSystemAdmin)
        {
            if (asset.CollectionId == null)
                return Results.Forbid(); // Only admins can edit orphan assets
            var canEdit = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Contributor);
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
        return Results.Ok(MapToDto(asset));
    }

    private static async Task<IResult> DeleteAsset(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization - require manager role (orphan assets require admin)
        if (!isSystemAdmin)
        {
            if (asset.CollectionId == null)
                return Results.Forbid(); // Only admins can delete orphan assets
            var canDelete = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Manager);
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

    private static AssetResponseDto MapToDto(Asset asset, string userRole = RoleHierarchy.Roles.Viewer)
    {
        return new AssetResponseDto
        {
            Id = asset.Id,
            CollectionId = asset.CollectionId,
            CollectionName = asset.Collection?.Name,
            AssetType = asset.AssetType,
            Status = asset.Status,
            Title = asset.Title,
            Description = asset.Description,
            Tags = asset.Tags,
            MetadataJson = asset.MetadataJson,
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            Sha256 = asset.Sha256,
            ThumbObjectKey = asset.ThumbObjectKey,
            MediumObjectKey = asset.MediumObjectKey,
            PosterObjectKey = asset.PosterObjectKey,
            CreatedAt = asset.CreatedAt,
            CreatedByUserId = asset.CreatedByUserId,
            UpdatedAt = asset.UpdatedAt,
            UserRole = userRole
        };
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

        // Check if user can access the primary collection or any linked collection (system admins bypass)
        if (!isSystemAdmin)
        {
            bool canAccess = false;
            
            if (asset.CollectionId != null)
            {
                canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Viewer);
            }
            
            // If no access via primary, check linked collections
            if (!canAccess)
            {
                var linkedCollections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);
                foreach (var linkedCollectionId in linkedCollections)
                {
                    if (await authService.CheckAccessAsync(userId, linkedCollectionId, RoleHierarchy.Roles.Viewer))
                    {
                        canAccess = true;
                        break;
                    }
                }
            }
            
            if (!canAccess)
                return Results.Forbid();
        }

        var collections = await assetCollectionRepo.GetCollectionsForAssetAsync(id, ct);

        var result = collections.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            description = c.Description,
            isPrimary = c.Id == asset.CollectionId
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
            // Check if user can contribute to the source collection (if it has one)
            if (asset.CollectionId != null)
            {
                var canAccessSource = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Contributor);
                if (!canAccessSource)
                    return Results.Json(ApiError.Forbidden("You don't have permission to manage this asset"), statusCode: 403);
            }
            else
            {
                // For orphan assets, check if user has access via any linked collection
                var linkedCollections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);
                bool canAccessAny = false;
                foreach (var linkedCollectionId in linkedCollections)
                {
                    if (await authService.CheckAccessAsync(userId, linkedCollectionId, RoleHierarchy.Roles.Contributor))
                    {
                        canAccessAny = true;
                        break;
                    }
                }
                if (!canAccessAny)
                    return Results.Json(ApiError.Forbidden("You don't have permission to manage this asset"), statusCode: 403);
            }

            var canAccessTarget = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor);
            if (!canAccessTarget)
                return Results.Json(ApiError.Forbidden("You don't have permission to add assets to this collection"), statusCode: 403);
        }

        // Check if it's the primary collection
        if (asset.CollectionId == collectionId)
            return Results.BadRequest(ApiError.BadRequest("Asset already belongs to this collection as its primary collection"));

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
            var canAccess = await authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor);
            if (!canAccess)
                return Results.Json(ApiError.Forbidden("You don't have permission to manage this asset in this collection"), statusCode: 403);
        }

        // If removing from primary collection, make the asset an orphan
        if (asset.CollectionId == collectionId)
        {
            asset.CollectionId = null;
            asset.UpdatedAt = DateTime.UtcNow;
            await assetRepository.UpdateAsync(asset, ct);
            return Results.Ok(new { message = "Asset removed from primary collection and is now an orphan" });
        }

        // Otherwise remove from the linked collection
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
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization (admins bypass, orphan assets are publicly viewable via linked collections)
        if (!isSystemAdmin && asset.CollectionId != null)
        {
            var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Viewer);
            if (!canAccess)
                return Results.Forbid();
        }

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
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        if (!isSystemAdmin && asset.CollectionId != null)
        {
            var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Viewer);
            if (!canAccess)
                return Results.Forbid();
        }

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
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        if (!isSystemAdmin && asset.CollectionId != null)
        {
            var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Viewer);
            if (!canAccess)
                return Results.Forbid();
        }

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
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        if (!isSystemAdmin && asset.CollectionId != null)
        {
            var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Viewer);
            if (!canAccess)
                return Results.Forbid();
        }

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
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var isSystemAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id, ct);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        if (!isSystemAdmin && asset.CollectionId != null)
        {
            var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId.Value, RoleHierarchy.Roles.Viewer);
            if (!canAccess)
                return Results.Forbid();
        }

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

    private static string DetermineAssetType(string contentType, string? extension)
    {
        // Check content type first
        if (!string.IsNullOrEmpty(contentType))
        {
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return Asset.TypeImage;
            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return Asset.TypeVideo;
            if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                return Asset.TypeDocument;
        }

        // Fall back to file extension
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" or ".tiff" or ".tif" or ".ico" => Asset.TypeImage,
            ".mp4" or ".avi" or ".mov" or ".wmv" or ".mkv" or ".webm" or ".flv" or ".m4v" => Asset.TypeVideo,
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" => Asset.TypeDocument,
            _ => Asset.TypeDocument
        };
    }

    #endregion
}
