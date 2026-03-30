using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Handles public (anonymous) share access: content retrieval, preview/download URL generation,
/// ZIP downloads, and access token creation.
/// </summary>
public sealed class PublicShareAccessService(
    IShareRepository shareRepo,
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    ICollectionRepository collectionRepo,
    IZipBuildService zipBuildService,
    IAuditService audit,
    IMinIOAdapter minioAdapter,
    IOptions<MinIOSettings> minioSettings,
    IDataProtectionProvider dataProtection,
    IHttpContextAccessor httpContextAccessor,
    ILogger<PublicShareAccessService> logger) : IPublicShareAccessService
{
    private readonly string _bucketName = minioSettings.Value.BucketName;

    public async Task<ServiceResult<ISharedContentDto>> GetSharedContentAsync(
        string token, string? password, int skip, int take, CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, ct);
        if (error != null) return error;

        await shareRepo.IncrementAccessAsync(share!.Id, ct);

        if (share.ScopeType == ShareScopeType.Asset)
        {
            var asset = await assetRepo.GetByIdAsync(share.ScopeId, ct);
            if (asset == null)
                return ServiceError.NotFound("Asset not found");

            return BuildSharedAssetDto(asset, token, share.PermissionsJson);
        }

        if (share.ScopeType == ShareScopeType.Collection)
        {
            var collection = await collectionRepo.GetByIdAsync(share.ScopeId, ct: ct);
            if (collection == null)
                return ServiceError.NotFound("Collection not found");

            var totalAssets = await assetRepo.CountByCollectionAsync(share.ScopeId, ct);
            var assets = await assetRepo.GetByCollectionAsync(share.ScopeId, skip, take, ct);
            var assetDtos = assets
                .Select(a => BuildSharedAssetDto(a, token, share.PermissionsJson, a.Id))
                .ToList();

            return new SharedCollectionDto
            {
                Id = collection.Id,
                Name = collection.Name,
                Description = collection.Description,
                Assets = assetDtos,
                TotalAssets = totalAssets,
                Permissions = share.PermissionsJson
            };
        }

        return ServiceError.BadRequest("Invalid share scope type");
    }

    public async Task<ServiceResult<string>> GetDownloadUrlAsync(
        string token, string? password, Guid? assetId, CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, ct);
        if (error != null) return error;

        if (!share!.PermissionsJson.TryGetValue("download", out var canDownload) || !canDownload)
            return ServiceError.Forbidden("Download permission not granted");

        var (targetAsset, resolveError) = await ResolveTargetAssetAsync(share, assetId, ct);
        if (resolveError != null) return resolveError;

        if (string.IsNullOrEmpty(targetAsset!.OriginalObjectKey))
            return ServiceError.BadRequest("Asset file not available");

        await shareRepo.IncrementAccessAsync(share.Id, ct);

        var ext = Path.GetExtension(targetAsset.OriginalObjectKey);
        var downloadFileName = $"{targetAsset.Title}{ext}";

        var presignedUrl = await minioAdapter.GetPresignedDownloadUrlAsync(
            _bucketName, targetAsset.OriginalObjectKey,
            Constants.Limits.PresignedDownloadExpirySec, forceDownload: true, downloadFileName, ct);

        return presignedUrl;
    }

    public async Task<ServiceResult<ZipDownloadEnqueuedResponse>> EnqueueDownloadAllAsync(
        string token, string? password, CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, ct);
        if (error != null) return error!;

        if (!share!.PermissionsJson.TryGetValue("download", out var canDownload) || !canDownload)
            return ServiceError.Forbidden("Download permission not granted");

        if (share.ScopeType != ShareScopeType.Collection)
            return ServiceError.BadRequest("Download all is only available for collection shares");

        var collection = await collectionRepo.GetByIdAsync(share.ScopeId, ct: ct);
        if (collection == null)
            return ServiceError.NotFound("Collection not found");

        await shareRepo.IncrementAccessAsync(share.Id, ct);

        return await zipBuildService.EnqueueShareZipAsync(
            share.ScopeId, share.TokenHash, collection.Name, ct);
    }

    public async Task<ServiceResult<string>> GetPreviewUrlAsync(
        string token, string? password, string? size, Guid? assetId, bool forceDownload, CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, ct);
        if (error != null) return error;

        var (targetAsset, resolveError) = await ResolveTargetAssetAsync(share!, assetId, ct);
        if (resolveError != null) return resolveError;

        // PDF → serve original for inline preview
        if (string.Equals(targetAsset!.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await GetPresignedUrl(targetAsset.OriginalObjectKey, forceDownload, ct);
        }

        // Video/audio without specific size → serve original for playback
        if (string.IsNullOrEmpty(size) && targetAsset.ContentType != null
            && (targetAsset.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || targetAsset.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)))
        {
            return await GetPresignedUrl(targetAsset.OriginalObjectKey, forceDownload, ct);
        }

        // Determine rendition key
        string? objectKey = size?.ToLower() switch
        {
            "thumb" => targetAsset.ThumbObjectKey,
            "medium" => targetAsset.MediumObjectKey,
            _ => targetAsset.MediumObjectKey ?? targetAsset.ThumbObjectKey
        };

        if (string.IsNullOrEmpty(objectKey))
            return ServiceError.NotFound("Preview not available");

        return await GetPresignedUrl(objectKey, forceDownload, ct);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ShareAccessTokenResponse>> CreateAccessTokenAsync(
        string token, string? password, CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, ct);
        if (error != null) return error;

        var lifetimeMinutes = Constants.Limits.ShareAccessTokenLifetimeMinutes;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(lifetimeMinutes).ToUnixTimeSeconds();

        var protector = dataProtection.CreateProtector(
            Constants.DataProtection.ShareAccessTokenProtector);
        var payload = $"share-access:{share!.TokenHash}:{expiresAt}";
        var accessToken = protector.Protect(payload);

        return new ShareAccessTokenResponse
        {
            AccessToken = accessToken,
            ExpiresInSeconds = lifetimeMinutes * 60
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<(Share? share, ServiceError? error)> ValidateAndGetShareAsync(
        string token, string? password, CancellationToken ct)
    {
        var tokenHash = ShareHelpers.ComputeTokenHash(token);
        var share = await shareRepo.GetByTokenHashAsync(tokenHash, ct);
        if (share == null)
            return (null, ServiceError.NotFound("Share link not found or invalid"));

        var accessError = ShareHelpers.ValidateShareAccess(share.RevokedAt, share.ExpiresAt);
        if (accessError != null)
        {
            var err = accessError == Constants.ShareErrorCodes.Revoked
                ? ServiceError.ShareRevoked()
                : ServiceError.ShareExpired();
            return (null, err);
        }

        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            // Check if the credential is a valid access token (pre-authorized)
            if (!string.IsNullOrEmpty(password) && IsValidAccessToken(tokenHash, password))
                return (share, null);

            if (string.IsNullOrEmpty(password))
                return (null, new ServiceError(401, "PASSWORD_REQUIRED", "Password required"));

            if (!BCrypt.Net.BCrypt.Verify(password, share.PasswordHash))
            {
                var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
                logger.LogWarning("Failed share password attempt for token hash {TokenHashPrefix}... from IP {IP}",
                    tokenHash[..Math.Min(8, tokenHash.Length)], ip ?? "unknown");
                await audit.LogAsync("share.password_failed", Constants.ScopeTypes.Share, share.Id, null,
                    new() { ["ip"] = ip ?? "unknown", ["tokenHashPrefix"] = tokenHash[..Math.Min(8, tokenHash.Length)] },
                    ct);
                return (null, new ServiceError(401, "UNAUTHORIZED", "Invalid password"));
            }
        }

        return (share, null);
    }

    private bool IsValidAccessToken(string expectedTokenHash, string possibleAccessToken)
    {
        try
        {
            var protector = dataProtection.CreateProtector(
                Constants.DataProtection.ShareAccessTokenProtector);
            var payload = protector.Unprotect(possibleAccessToken);
            var parts = payload.Split(':');

            if (parts.Length != 3 || parts[0] != "share-access")
                return false;

            if (parts[1] != expectedTokenHash)
                return false;

            if (!long.TryParse(parts[2], out var expirySeconds))
                return false;

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expirySeconds;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static SharedAssetDto BuildSharedAssetDto(
        Asset asset, string token, Dictionary<string, bool> permissions, Guid? assetId = null)
    {
        // Strip GPS coordinates from shared metadata to protect location privacy (CWE-359)
        var publicMetadata = (asset.MetadataJson ?? new())
            .Where(kvp => !kvp.Key.StartsWith("gps", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return new SharedAssetDto
        {
            Id = asset.Id,
            Title = asset.Title,
            Description = asset.Description,
            Copyright = asset.Copyright,
            AssetType = asset.AssetType.ToDbString(),
            ContentType = asset.ContentType,
            SizeBytes = asset.SizeBytes,
            ThumbnailUrl = BuildPreviewUrl(token, "thumb", asset.ThumbObjectKey, assetId),
            MediumUrl = BuildPreviewUrl(token, "medium", asset.MediumObjectKey, assetId),
            MetadataJson = publicMetadata,
            Permissions = permissions
        };
    }

    private static string? BuildPreviewUrl(string token, string size, string? objectKey, Guid? assetId)
    {
        if (string.IsNullOrEmpty(objectKey))
            return null;
        var assetIdQuery = assetId.HasValue ? $"&assetId={assetId.Value}" : "";
        return $"/api/v1/shares/{token}/preview?size={size}{assetIdQuery}";
    }

    private async Task<(Asset? asset, ServiceError? error)> ResolveTargetAssetAsync(
        Share share, Guid? assetId, CancellationToken ct)
    {
        if (share.ScopeType == ShareScopeType.Asset)
        {
            var asset = await assetRepo.GetByIdAsync(share.ScopeId, ct);
            return asset != null
                ? (asset, null)
                : (null, ServiceError.NotFound("Asset not found"));
        }

        if (share.ScopeType == ShareScopeType.Collection)
        {
            if (!assetId.HasValue)
                return (null, ServiceError.BadRequest("assetId query parameter is required for collection shares"));

            var asset = await assetRepo.GetByIdAsync(assetId.Value, ct);
            if (asset == null)
                return (null, ServiceError.NotFound("Asset not found in this shared collection"));

            var belongsToCollection = await assetCollectionRepo.BelongsToCollectionAsync(assetId.Value, share.ScopeId, ct);
            if (!belongsToCollection)
                return (null, ServiceError.NotFound("Asset not found in this shared collection"));

            return (asset, null);
        }

        return (null, ServiceError.BadRequest("Invalid share scope type"));
    }

    private async Task<ServiceResult<string>> GetPresignedUrl(string objectKey, bool forceDownload, CancellationToken ct)
    {
        var url = await minioAdapter.GetPresignedDownloadUrlAsync(
            _bucketName, objectKey, Constants.Limits.PresignedDownloadExpirySec, forceDownload, null, ct);
        return url;
    }
}
