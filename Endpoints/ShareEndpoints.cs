using System.IO.Compression;
using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Application.Services.Email.Templates;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;

namespace AssetHub.Endpoints;

public static class ShareEndpoints
{
    /// <summary>
    /// Extracts the share password from X-Share-Password header first, then query string fallback.
    /// Header is preferred to avoid passwords appearing in server logs and browser history.
    /// Query string fallback is kept for HTML elements (img/video/iframe src) that can't set headers.
    /// </summary>
    private static string? GetSharePassword(HttpContext httpContext, string? queryPassword)
    {
        var headerPassword = httpContext.Request.Headers["X-Share-Password"].FirstOrDefault();
        return headerPassword ?? queryPassword;
    }

    /// <summary>
    /// Common validation for all public share endpoints: token lookup, access check, and password verification.
    /// Returns either a valid Share entity or an IResult error to return immediately.
    /// </summary>
    private static async Task<(Share? share, IResult? error)> ValidateAndGetShareAsync(
        string token,
        string? password,
        HttpContext httpContext,
        IShareRepository shareRepository,
        CancellationToken ct)
    {
        var effectivePassword = GetSharePassword(httpContext, password);
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        var share = await shareRepository.GetByTokenHashAsync(tokenHash, ct);
        if (share == null)
            return (null, Results.NotFound("Share link not found or invalid"));

        var accessError = ShareHelpers.ValidateShareAccess(share.RevokedAt, share.ExpiresAt);
        if (accessError != null)
            return (null, Results.BadRequest(accessError));

        // Check password if required
        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            if (string.IsNullOrEmpty(effectivePassword))
                return (null, Results.Json(new PasswordRequiredResponse { RequiresPassword = true }, statusCode: 401));

            if (!BCrypt.Net.BCrypt.Verify(effectivePassword, share.PasswordHash))
                return (null, Results.Unauthorized());
        }

        return (share, null);
    }

    public static void MapShareEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/shares")
            .DisableAntiforgery() // API uses JWT Bearer auth, not cookies with CSRF tokens
            .WithTags("Shares");

        // Public endpoints (no auth required)
        group.MapGet("{token}", GetSharedAsset)
            .WithName("GetSharedAsset")
            .AllowAnonymous();
        group.MapGet("{token}/download", DownloadSharedAsset)
            .WithName("DownloadSharedAsset")
            .AllowAnonymous();
        group.MapGet("{token}/download-all", DownloadAllSharedAssets)
            .WithName("DownloadAllSharedAssets")
            .AllowAnonymous();
        group.MapGet("{token}/preview", PreviewSharedAsset)
            .WithName("PreviewSharedAsset")
            .AllowAnonymous();

        // Protected endpoints
        var authGroup = group.RequireAuthorization();
        authGroup.MapPost("", CreateShare).WithName("CreateShare");
        authGroup.MapDelete("{id}", RevokeShare).WithName("RevokeShare");
        authGroup.MapPut("{id}/password", UpdateSharePassword).WithName("UpdateSharePassword");
    }

    private static async Task<IResult> CreateShare(
        CreateShareDto dto,
        [FromServices] IShareService shareService,
        [FromServices] ICollectionAuthorizationService authService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();

        // Validate scope and resolve authorization target
        var validation = await shareService.ValidateScopeAsync(dto, ct);
        if (!validation.IsValid)
        {
            return validation.ErrorStatusCode == 404
                ? Results.NotFound(ApiError.NotFound(validation.ErrorMessage!))
                : Results.BadRequest(ApiError.BadRequest(validation.ErrorMessage!));
        }

        // Authorization: User must have contributor+ role to share
        if (!await authService.CheckAccessAsync(userId, validation.CollectionIdToCheck!.Value, RoleHierarchy.Roles.Contributor, ct))
            return Results.Json(ApiError.Forbidden("You don't have permission to share this resource"), statusCode: 403);

        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var result = await shareService.CreateShareAsync(dto, userId, baseUrl, httpContext, ct);

        return Results.Created($"/api/shares/{result.Response.Id}", result.Response);
    }

    private static async Task<IResult> GetSharedAsset(
        string token,
        string? password,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] ICollectionRepository collectionRepository,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct,
        int skip = 0,
        int take = 50)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, httpContext, shareRepository, ct);
        if (error != null) return error;

        // Atomic access tracking — single UPDATE avoids race conditions
        await shareRepository.IncrementAccessAsync(share!.Id, ct);

        var bucketName = StorageConfig.GetBucketName(configuration);

        if (share.ScopeType == Constants.ScopeTypes.Asset)
        {
            var asset = await assetRepository.GetByIdAsync(share.ScopeId, ct);
            if (asset == null)
                return Results.NotFound("Asset not found");

            // Use API URLs for thumbnails to hide MinIO backend
            string? thumbnailUrl = null;
            string? mediumUrl = null;

            if (!string.IsNullOrEmpty(asset.ThumbObjectKey))
                thumbnailUrl = $"/api/shares/{token}/preview?size=thumb";
            if (!string.IsNullOrEmpty(asset.MediumObjectKey))
                mediumUrl = $"/api/shares/{token}/preview?size=medium";

            return Results.Ok(new SharedAssetDto
            {
                Id = asset.Id,
                Title = asset.Title,
                Description = asset.Description,
                AssetType = asset.AssetType,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes,
                ThumbnailUrl = thumbnailUrl,
                MediumUrl = mediumUrl,
                MetadataJson = asset.MetadataJson,
                Permissions = share.PermissionsJson
            });
        }
        else if (share.ScopeType == Constants.ScopeTypes.Collection)
        {
            var collection = await collectionRepository.GetByIdAsync(share.ScopeId, ct: ct);
            if (collection == null)
                return Results.NotFound("Collection not found");

            var totalAssets = await assetRepository.CountByCollectionAsync(share.ScopeId, ct);
            var assets = await assetRepository.GetByCollectionAsync(share.ScopeId, skip, take, ct);
            var assetDtos = new List<SharedAssetDto>();

            foreach (var asset in assets)
            {
                string? thumbnailUrl = null;
                string? mediumUrl = null;

                if (!string.IsNullOrEmpty(asset.ThumbObjectKey))
                    thumbnailUrl = $"/api/shares/{token}/preview?size=thumb&assetId={asset.Id}";
                if (!string.IsNullOrEmpty(asset.MediumObjectKey))
                    mediumUrl = $"/api/shares/{token}/preview?size=medium&assetId={asset.Id}";

                assetDtos.Add(new SharedAssetDto
                {
                    Id = asset.Id,
                    Title = asset.Title,
                    Description = asset.Description,
                    AssetType = asset.AssetType,
                    ContentType = asset.ContentType,
                    SizeBytes = asset.SizeBytes,
                    ThumbnailUrl = thumbnailUrl,
                    MediumUrl = mediumUrl,
                    MetadataJson = asset.MetadataJson,
                    Permissions = share.PermissionsJson
                });
            }

            return Results.Ok(new SharedCollectionDto
            {
                Id = collection.Id,
                Name = collection.Name,
                Description = collection.Description,
                Assets = assetDtos,
                TotalAssets = totalAssets,
                Permissions = share.PermissionsJson
            });
        }

        return Results.BadRequest("Invalid share scope type");
    }

    private static async Task<IResult> DownloadSharedAsset(
        string token,
        string? password,
        Guid? assetId,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, httpContext, shareRepository, ct);
        if (error != null) return error;

        // Check if download permission is granted
        if (!share!.PermissionsJson.TryGetValue("download", out var canDownload) || !canDownload)
            return Results.Forbid();

        var bucketName = StorageConfig.GetBucketName(configuration);
        Asset? targetAsset = null;

        if (share.ScopeType == Constants.ScopeTypes.Asset)
        {
            targetAsset = await assetRepository.GetByIdAsync(share.ScopeId, ct);
        }
        else if (share.ScopeType == Constants.ScopeTypes.Collection)
        {
            // For collection shares, assetId must be provided to specify which asset to download
            if (!assetId.HasValue)
                return Results.BadRequest("assetId query parameter is required for collection share downloads");

            targetAsset = await assetRepository.GetByIdAsync(assetId.Value, ct);

            // Verify the asset belongs to the shared collection using join table
            if (targetAsset == null)
                return Results.NotFound("Asset not found in this shared collection");
            
            var belongsToCollection = await assetCollectionRepo.BelongsToCollectionAsync(assetId.Value, share.ScopeId, ct);
            if (!belongsToCollection)
                return Results.NotFound("Asset not found in this shared collection");
        }

        if (targetAsset == null)
            return Results.NotFound("Asset not found");

        if (string.IsNullOrEmpty(targetAsset.OriginalObjectKey))
            return Results.BadRequest("Asset file not available");

        // Atomic access tracking — single UPDATE avoids race conditions
        await shareRepository.IncrementAccessAsync(share.Id, ct);

        // Redirect to presigned URL — file goes directly from MinIO to browser
        var presignedUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, targetAsset.OriginalObjectKey, expirySeconds: 300, ct);
        return Results.Redirect(presignedUrl);
    }

    private static async Task<IResult> DownloadAllSharedAssets(
        string token,
        string? password,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] ICollectionRepository collectionRepository,
        [FromServices] IZipDownloadService zipService,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, httpContext, shareRepository, ct);
        if (error != null) return error;

        if (!share!.PermissionsJson.TryGetValue("download", out var canDownload) || !canDownload)
            return Results.Forbid();

        if (share.ScopeType != Constants.ScopeTypes.Collection)
            return Results.BadRequest("Download all is only available for collection shares");

        var collection = await collectionRepository.GetByIdAsync(share.ScopeId, ct: ct);
        if (collection == null)
            return Results.NotFound("Collection not found");

        var assets = await assetRepository.GetByCollectionAsync(share.ScopeId, 0, 1000, ct);
        if (!assets.Any())
            return Results.BadRequest("No assets in collection");

        var bucketName = StorageConfig.GetBucketName(configuration);

        // Atomic access tracking — single UPDATE avoids race conditions
        await shareRepository.IncrementAccessAsync(share.Id, ct);

        var zipFileName = $"{collection.Name.Replace(" ", "_")}_assets.zip";
        await zipService.StreamAssetsAsZipAsync(assets, bucketName, zipFileName, httpContext, ct);

        return Results.Empty;
    }

    private static async Task<IResult> PreviewSharedAsset(
        string token,
        string? password,
        string? size,
        Guid? assetId,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, httpContext, shareRepository, ct);
        if (error != null) return error;

        var bucketName = StorageConfig.GetBucketName(configuration);
        Asset? targetAsset = null;

        if (share!.ScopeType == Constants.ScopeTypes.Asset)
        {
            targetAsset = await assetRepository.GetByIdAsync(share.ScopeId, ct);
        }
        else if (share.ScopeType == Constants.ScopeTypes.Collection)
        {
            // For collection shares, assetId must be provided
            if (!assetId.HasValue)
                return Results.BadRequest("assetId query parameter is required for collection share previews");

            targetAsset = await assetRepository.GetByIdAsync(assetId.Value, ct);

            // Verify the asset belongs to the shared collection using join table
            if (targetAsset == null)
                return Results.NotFound("Asset not found in this shared collection");
            
            var belongsToCollection = await assetCollectionRepo.BelongsToCollectionAsync(assetId.Value, share.ScopeId, ct);
            if (!belongsToCollection)
                return Results.NotFound("Asset not found in this shared collection");
        }

        if (targetAsset == null)
            return Results.NotFound("Asset not found");

        // For PDF files, redirect to presigned URL for inline preview
        if (string.Equals(targetAsset.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, targetAsset.OriginalObjectKey, expirySeconds: 300, ct);
            return Results.Redirect(pdfUrl);
        }

        // For video/audio files without a specific rendition size, serve the original
        // so <video>/<audio> elements can play the actual media file.
        if (string.IsNullOrEmpty(size)
            && targetAsset.ContentType != null
            && (targetAsset.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || targetAsset.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)))
        {
            var mediaUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, targetAsset.OriginalObjectKey, expirySeconds: 300, ct);
            return Results.Redirect(mediaUrl);
        }

        // Determine which object key to use based on size
        string? objectKey = size?.ToLower() switch
        {
            "thumb" => targetAsset.ThumbObjectKey,
            "medium" => targetAsset.MediumObjectKey,
            _ => targetAsset.MediumObjectKey ?? targetAsset.ThumbObjectKey
        };

        if (string.IsNullOrEmpty(objectKey))
            return Results.NotFound("Preview not available");

        // Redirect to presigned URL — file goes directly from MinIO to browser
        var presignedUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, objectKey, expirySeconds: 300, ct);
        return Results.Redirect(presignedUrl);
    }

    private static async Task<IResult> RevokeShare(
        Guid id,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAuditService audit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var share = await shareRepository.GetByIdAsync(id, ct);
        if (share == null)
            return Results.NotFound(ApiError.NotFound("Share not found"));

        // Check authorization (owner can revoke)
        var userId = httpContext.User.GetRequiredUserId();
        if (share.CreatedByUserId != userId)
            return Results.Json(ApiError.Forbidden("You don't have permission to revoke this share"), statusCode: 403);

        // Mark as revoked instead of deleting (for audit trail)
        share.RevokedAt = DateTime.UtcNow;
        await shareRepository.UpdateAsync(share, ct);

        await audit.LogAsync("share.revoked", "share", id, userId,
            new() { ["scopeType"] = share.ScopeType, ["scopeId"] = share.ScopeId }, httpContext, ct);

        return Results.NoContent();
    }

    /// <summary>
    /// Updates the password for an existing share.
    /// </summary>
    private static async Task<IResult> UpdateSharePassword(
        Guid id,
        [FromBody] UpdateSharePasswordDto dto,
        [FromServices] IShareRepository shareRepository,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var share = await shareRepository.GetByIdAsync(id, ct);
        if (share == null)
            return Results.NotFound(ApiError.NotFound("Share not found"));

        // Check authorization (owner or admin can update)
        var userId = httpContext.User.GetRequiredUserId();
        var isAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        if (share.CreatedByUserId != userId && !isAdmin)
            return Results.Json(ApiError.Forbidden("You don't have permission to update this share"), statusCode: 403);

        if (string.IsNullOrWhiteSpace(dto.Password))
            return Results.BadRequest(ApiError.BadRequest("Password cannot be empty"));

        share.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
        await shareRepository.UpdateAsync(share, ct);

        return Results.Ok(new MessageResponse("Password updated successfully"));
    }
}
