using System.Security.Cryptography;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Standard DI shape: asset repo + asset-collection repo + auth service + resizer + MinIO + 2 IOptions + scoped CurrentUser + logger. Bundling them obscures intent.")]
public sealed class RenditionService(
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    ICollectionAuthorizationService authService,
    IRenditionImageResizer resizer,
    IMinIOAdapter minio,
    IOptions<RenditionSettings> renditionSettings,
    IOptions<MinIOSettings> minioSettings,
    CurrentUser currentUser,
    ILogger<RenditionService> logger) : IRenditionService
{
    private string Bucket => minioSettings.Value.BucketName;

    public async Task<ServiceResult<RenditionResult>> GetOrGenerateAsync(
        Guid assetId, RenditionRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return ServiceError.Forbidden();

        // ── Validation against the allowlist ───────────────────────────
        var settings = renditionSettings.Value;
        if (request.Width is null && request.Height is null)
            return ServiceError.BadRequest("Specify at least one of width or height.");
        if (request.Width is int w && !settings.AllowedWidths.Contains(w))
            return ServiceError.BadRequest(
                $"width must be one of: {string.Join(", ", settings.AllowedWidths)}.");
        if (request.Height is int h && !settings.AllowedHeights.Contains(h))
            return ServiceError.BadRequest(
                $"height must be one of: {string.Join(", ", settings.AllowedHeights)}.");
        if (!settings.AllowedFitModes.Contains(request.FitMode, StringComparer.OrdinalIgnoreCase))
            return ServiceError.BadRequest(
                $"fit must be one of: {string.Join(", ", settings.AllowedFitModes)}.");
        if (!settings.AllowedFormats.Contains(request.Format, StringComparer.OrdinalIgnoreCase))
            return ServiceError.BadRequest(
                $"fmt must be one of: {string.Join(", ", settings.AllowedFormats)}.");

        // ── Asset + ACL ────────────────────────────────────────────────
        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found.");
        if (asset.AssetType != AssetType.Image)
            return ServiceError.BadRequest("Renditions are only available for image assets.");
        if (string.IsNullOrEmpty(asset.OriginalObjectKey))
            return ServiceError.BadRequest("Asset has no original to render from.");

        if (!await CanAccessAssetAsync(assetId, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        // ── Cache key — deterministic per (asset, params) ──────────────
        var contentType = ContentTypeFor(request.Format);
        var ext = ExtensionFor(request.Format);
        var paramsHash = HashParams(request);
        var cacheKey = $"{Constants.StoragePrefixes.RenditionsOnDemand}/{assetId}/{paramsHash}{ext}";

        // ── Cache hit? ──────────────────────────────────────────────────
        if (await minio.ExistsAsync(Bucket, cacheKey, ct))
        {
            var url = await minio.GetPresignedDownloadUrlAsync(
                Bucket, cacheKey, settings.PresignedUrlExpirySeconds,
                cancellationToken: ct);
            logger.LogDebug(
                "Rendition cache hit for asset {AssetId}: {CacheKey}", assetId, cacheKey);
            return new RenditionResult(url, contentType, CacheHit: true);
        }

        // Cache miss — synchronous generation via the resizer abstraction
        // (production wiring goes through ImageMagick; tests substitute a
        // mock so they run without it on the box).
        try
        {
            await resizer.ResizeAsync(new RenditionResizeRequest(
                SourceObjectKey: asset.OriginalObjectKey,
                TargetObjectKey: cacheKey,
                TargetContentType: contentType,
                Width: request.Width,
                Height: request.Height,
                FitMode: request.FitMode,
                Format: request.Format,
                Quality: settings.Quality), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Rendition generation failed for asset {AssetId} with params {Params}",
                assetId, paramsHash);
            return ServiceError.Server("Failed to generate rendition.");
        }

        var generatedUrl = await minio.GetPresignedDownloadUrlAsync(
            Bucket, cacheKey, settings.PresignedUrlExpirySeconds,
            cancellationToken: ct);

        logger.LogInformation(
            "Rendition generated for asset {AssetId} → {CacheKey} ({Format} {W}x{H} {Fit})",
            assetId, cacheKey, request.Format, request.Width, request.Height, request.FitMode);

        return new RenditionResult(generatedUrl, contentType, CacheHit: false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task<bool> CanAccessAssetAsync(Guid assetId, string requiredRole, CancellationToken ct)
    {
        if (currentUser.IsSystemAdmin) return true;
        var collections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        var accessible = await authService.FilterAccessibleAsync(
            currentUser.UserId, collections, requiredRole, ct);
        return accessible.Count > 0;
    }

    /// <summary>
    /// Deterministic per-request hash so the cache key is stable across
    /// identical requests but differs the moment any param changes.
    /// 12 hex chars = 48 bits — collision probability is negligible at
    /// the per-asset scope (we never share keys across assets).
    /// </summary>
    private static string HashParams(RenditionRequest req)
    {
        var canonical =
            $"w={req.Width?.ToString() ?? "_"}|" +
            $"h={req.Height?.ToString() ?? "_"}|" +
            $"fit={req.FitMode.ToLowerInvariant()}|" +
            $"fmt={req.Format.ToLowerInvariant()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(bytes)[..12];
    }

    private static string ContentTypeFor(string format) => format.ToLowerInvariant() switch
    {
        "png" => "image/png",
        "webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static string ExtensionFor(string format) => format.ToLowerInvariant() switch
    {
        "png" => ".png",
        "webp" => ".webp",
        _ => ".jpg"
    };

}
