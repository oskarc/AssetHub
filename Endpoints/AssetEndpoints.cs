using System.Security.Claims;
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
        group.MapGet("{id}", GetAsset).WithName("GetAsset");
        group.MapPost("", UploadAsset).WithName("UploadAsset");
        group.MapPatch("{id}", UpdateAsset).WithName("UpdateAsset");
        group.MapDelete("{id}", DeleteAsset).WithName("DeleteAsset");
        group.MapGet("collection/{collectionId}", GetAssetsByCollection).WithName("GetAssetsByCollection");

        // Rendition/download endpoints
        group.MapGet("{id}/download", DownloadOriginal).WithName("DownloadOriginal");
        group.MapGet("{id}/thumb", GetThumbnail).WithName("GetThumbnail");
        group.MapGet("{id}/medium", GetMedium).WithName("GetMedium");
        group.MapGet("{id}/poster", GetPoster).WithName("GetPoster");
    }

    private static async Task<IResult> GetAssets(
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        int skip = 0,
        int take = 50)
    {
        // In a production system, you'd filter by user's accessible collections
        var assets = await assetRepository.GetByStatusAsync(Asset.StatusReady, skip, take);
        var dtos = assets.Select(MapToDto).ToList();
        return Results.Ok(dtos);
    }

    private static async Task<IResult> GetAsset(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository)
    {
        var asset = await assetRepository.GetByIdAsync(id);
        if (asset == null)
            return Results.NotFound();

        return Results.Ok(MapToDto(asset));
    }

    private static async Task<IResult> GetAssetsByCollection(
        Guid collectionId,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        string? query = null,
        string? type = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50)
    {
        var userId = httpContext.User.GetUserIdOrDefault();

        // Check if user can access this collection
        var canAccess = await authService.CheckAccessAsync(userId, collectionId, "viewer");
        if (!canAccess)
            return Results.Forbid();

        var (assets, total) = await assetRepository.SearchAsync(collectionId, query, type, sortBy, skip, take);
        var dtos = assets.Select(MapToDto).ToList();
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
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var bucketName = GetBucketName(configuration);

        // Check if user can contribute to this collection
        var canContribute = await authService.CheckAccessAsync(userId, collectionId, "contributor");
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
            await minioAdapter.UploadAsync(bucketName, objectKey, stream, file.ContentType);

            // Save asset to database
            await assetRepository.CreateAsync(asset);

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
        CreateAssetDto dto,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();

        var asset = await assetRepository.GetByIdAsync(id);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        var canEdit = await authService.CheckAccessAsync(userId, asset.CollectionId, "contributor");
        if (!canEdit)
            return Results.Forbid();

        asset.Title = dto.Title;
        asset.Description = dto.Description;
        asset.Tags = dto.Tags ?? new();
        if (dto.MetadataJson != null)
            asset.MetadataJson = dto.MetadataJson;
        asset.UpdatedAt = DateTime.UtcNow;

        await assetRepository.UpdateAsync(asset);
        return Results.Ok(MapToDto(asset));
    }

    private static async Task<IResult> DeleteAsset(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id);
        if (asset == null)
            return Results.NotFound();

        // Check authorization - require manager role
        var canDelete = await authService.CheckAccessAsync(userId, asset.CollectionId, "manager");
        if (!canDelete)
            return Results.Forbid();

        try
        {
            // Delete from MinIO
            await minioAdapter.DeleteAsync(bucketName, asset.OriginalObjectKey);
            if (asset.ThumbObjectKey != null)
                await minioAdapter.DeleteAsync(bucketName, asset.ThumbObjectKey);
            if (asset.MediumObjectKey != null)
                await minioAdapter.DeleteAsync(bucketName, asset.MediumObjectKey);
            if (asset.PosterObjectKey != null)
                await minioAdapter.DeleteAsync(bucketName, asset.PosterObjectKey);

            // Delete from database
            await assetRepository.DeleteAsync(id);

            return Results.NoContent();
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Deletion failed: {ex.Message}");
        }
    }

    private static AssetResponseDto MapToDto(Asset asset)
    {
        return new AssetResponseDto
        {
            Id = asset.Id,
            CollectionId = asset.CollectionId,
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
            UpdatedAt = asset.UpdatedAt
        };
    }

    #region Rendition Endpoints

    private static async Task<IResult> DownloadOriginal(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId, "viewer");
        if (!canAccess)
            return Results.Forbid();

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.OriginalObjectKey);
            var fileName = asset.Title + Path.GetExtension(asset.OriginalObjectKey);
            return Results.File(stream, asset.ContentType, fileName);
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Download failed: {ex.Message}");
        }
    }

    private static async Task<IResult> GetThumbnail(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        [Microsoft.AspNetCore.Mvc.FromServices] IConfiguration configuration,
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId, "viewer");
        if (!canAccess)
            return Results.Forbid();

        if (string.IsNullOrEmpty(asset.ThumbObjectKey))
            return Results.NotFound("Thumbnail not available");

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.ThumbObjectKey);
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
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId, "viewer");
        if (!canAccess)
            return Results.Forbid();

        if (string.IsNullOrEmpty(asset.MediumObjectKey))
        {
            // Fall back to original if medium not available
            try
            {
                var stream = await minioAdapter.DownloadAsync(bucketName, asset.OriginalObjectKey);
                return Results.File(stream, asset.ContentType);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Failed to get asset: {ex.Message}");
            }
        }

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.MediumObjectKey);
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
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();
        var bucketName = GetBucketName(configuration);

        var asset = await assetRepository.GetByIdAsync(id);
        if (asset == null)
            return Results.NotFound();

        // Check authorization
        var canAccess = await authService.CheckAccessAsync(userId, asset.CollectionId, "viewer");
        if (!canAccess)
            return Results.Forbid();

        if (string.IsNullOrEmpty(asset.PosterObjectKey))
            return Results.NotFound("Poster not available");

        try
        {
            var stream = await minioAdapter.DownloadAsync(bucketName, asset.PosterObjectKey);
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
