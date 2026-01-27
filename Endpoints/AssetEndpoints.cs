using System.Security.Claims;
using Dam.Application.Dtos;
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
            .WithTags("Assets");

        group.MapGet("", GetAssets).WithName("GetAssets");
        group.MapGet("{id}", GetAsset).WithName("GetAsset");
        group.MapPost("", UploadAsset).WithName("UploadAsset");
        group.MapPatch("{id}", UpdateAsset).WithName("UpdateAsset");
        group.MapDelete("{id}", DeleteAsset).WithName("DeleteAsset");
        group.MapGet("collection/{collectionId}", GetAssetsByCollection).WithName("GetAssetsByCollection");
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
        int skip = 0,
        int take = 50)
    {
        var userId = httpContext.User.GetUserIdOrDefault();

        // Check if user can access this collection
        var canAccess = await authService.CheckAccessAsync(userId, collectionId, "viewer");
        if (!canAccess)
            return Results.Forbid();

        var assets = await assetRepository.GetByCollectionAsync(collectionId, skip, take);
        var dtos = assets.Select(MapToDto).ToList();
        return Results.Ok(new
        {
            collectionId,
            total = await assetRepository.CountByCollectionAsync(collectionId),
            items = dtos
        });
    }

    private static async Task<IResult> UploadAsset(
        IFormFile file,
        Guid collectionId,
        string title,
        [Microsoft.AspNetCore.Mvc.FromServices] IAssetRepository assetRepository,
        [Microsoft.AspNetCore.Mvc.FromServices] IMinIOAdapter minioAdapter,
        [Microsoft.AspNetCore.Mvc.FromServices] IMediaProcessingService mediaProcessingService,
        [Microsoft.AspNetCore.Mvc.FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();

        // Check if user can contribute to this collection
        var canContribute = await authService.CheckAccessAsync(userId, collectionId, "contributor");
        if (!canContribute)
            return Results.Forbid();

        if (file == null || file.Length == 0)
            return Results.BadRequest("File is required");

        try
        {
            // Determine asset type from content type
            var assetType = file.ContentType switch
            {
                var ct when ct.StartsWith("image/") => Asset.TypeImage,
                var ct when ct.StartsWith("video/") => Asset.TypeVideo,
                var ct when ct.StartsWith("application/pdf") => Asset.TypeDocument,
                _ => Asset.TypeDocument
            };

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
            await minioAdapter.UploadAsync("assethub-dev", objectKey, stream, file.ContentType);

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
        HttpContext httpContext)
    {
        var userId = httpContext.User.GetUserIdOrDefault();

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
            await minioAdapter.DeleteAsync("assethub-dev", asset.OriginalObjectKey);
            if (asset.ThumbObjectKey != null)
                await minioAdapter.DeleteAsync("assethub-dev", asset.ThumbObjectKey);
            if (asset.MediumObjectKey != null)
                await minioAdapter.DeleteAsync("assethub-dev", asset.MediumObjectKey);
            if (asset.PosterObjectKey != null)
                await minioAdapter.DeleteAsync("assethub-dev", asset.PosterObjectKey);

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
}
