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
/// Groups repository and domain-service dependencies for <see cref="ShareAccessService"/>
/// to keep the constructor parameter count manageable.
/// </summary>
public sealed record ShareAccessDependencies(
    IShareRepository ShareRepo,
    IAssetRepository AssetRepo,
    IAssetCollectionRepository AssetCollectionRepo,
    ICollectionRepository CollectionRepo,
    ICollectionAuthorizationService AuthService,
    IShareService ShareService,
    IZipBuildService ZipBuildService,
    IAuditService Audit);

/// <summary>
/// Handles public share access and authenticated share management.
/// </summary>
public class ShareAccessService : IPublicShareAccessService, IAuthenticatedShareAccessService
{
    private readonly IShareRepository _shareRepo;
    private readonly IAssetRepository _assetRepo;
    private readonly IAssetCollectionRepository _assetCollectionRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IShareService _shareService;
    private readonly IMinIOAdapter _minioAdapter;
    private readonly IZipBuildService _zipBuildService;
    private readonly IAuditService _audit;
    private readonly string _bucketName;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly CurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ShareAccessService> _logger;

    public ShareAccessService(
        ShareAccessDependencies deps,
        IMinIOAdapter minioAdapter,
        IOptions<MinIOSettings> minioSettings,
        IDataProtectionProvider dataProtection,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ShareAccessService> logger,
        CurrentUser currentUser)
    {
        _shareRepo = deps.ShareRepo;
        _assetRepo = deps.AssetRepo;
        _assetCollectionRepo = deps.AssetCollectionRepo;
        _collectionRepo = deps.CollectionRepo;
        _authService = deps.AuthService;
        _shareService = deps.ShareService;
        _zipBuildService = deps.ZipBuildService;
        _audit = deps.Audit;
        _minioAdapter = minioAdapter;
        _bucketName = minioSettings.Value.BucketName;
        _dataProtection = dataProtection;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _currentUser = currentUser;
    }

    // ── Public operations ────────────────────────────────────────────────────

    public async Task<ServiceResult<ISharedContentDto>> GetSharedContentAsync(
        string token, string? password, int skip, int take, CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, ct);
        if (error != null) return error;

        await _shareRepo.IncrementAccessAsync(share!.Id, ct);

        if (share.ScopeType == ShareScopeType.Asset)
        {
            var asset = await _assetRepo.GetByIdAsync(share.ScopeId, ct);
            if (asset == null)
                return ServiceError.NotFound("Asset not found");

            return BuildSharedAssetDto(asset, token, share.PermissionsJson);
        }

        if (share.ScopeType == ShareScopeType.Collection)
        {
            var collection = await _collectionRepo.GetByIdAsync(share.ScopeId, ct: ct);
            if (collection == null)
                return ServiceError.NotFound("Collection not found");

            var totalAssets = await _assetRepo.CountByCollectionAsync(share.ScopeId, ct);
            var assets = await _assetRepo.GetByCollectionAsync(share.ScopeId, skip, take, ct);
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

        await _shareRepo.IncrementAccessAsync(share.Id, ct);

        // Build friendly download filename from asset title
        var ext = Path.GetExtension(targetAsset.OriginalObjectKey);
        var downloadFileName = $"{targetAsset.Title}{ext}";

        var presignedUrl = await _minioAdapter.GetPresignedDownloadUrlAsync(
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

        var collection = await _collectionRepo.GetByIdAsync(share.ScopeId, ct: ct);
        if (collection == null)
            return ServiceError.NotFound("Collection not found");

        await _shareRepo.IncrementAccessAsync(share.Id, ct);

        return await _zipBuildService.EnqueueShareZipAsync(
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

    /// <summary>
    /// Creates a short-lived access token after validating the share password.
    /// The token is a data-protected payload containing the share token hash
    /// and an expiry timestamp — suitable for use in query strings without
    /// exposing the actual password.
    /// </summary>
    public async Task<ServiceResult<ShareAccessTokenResponse>> CreateAccessTokenAsync(
        string token, string? password, CancellationToken ct)
    {
        var (share, error) = await ValidateAndGetShareAsync(token, password, ct);
        if (error != null) return error;

        var lifetimeMinutes = Constants.Limits.ShareAccessTokenLifetimeMinutes;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(lifetimeMinutes).ToUnixTimeSeconds();

        var protector = _dataProtection.CreateProtector(
            Constants.DataProtection.ShareAccessTokenProtector);
        var payload = $"share-access:{share!.TokenHash}:{expiresAt}";
        var accessToken = protector.Protect(payload);

        return new ShareAccessTokenResponse
        {
            AccessToken = accessToken,
            ExpiresInSeconds = lifetimeMinutes * 60
        };
    }

    // ── Authenticated operations ─────────────────────────────────────────────

    public async Task<ServiceResult<ShareResponseDto>> CreateShareAsync(
        CreateShareDto dto, string baseUrl, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return ServiceError.Forbidden("Authentication required to create shares");

        var validation = await _shareService.ValidateScopeAsync(dto, ct);
        if (!validation.IsValid)
        {
            return validation.ErrorStatusCode == 404
                ? ServiceError.NotFound(validation.ErrorMessage!)
                : ServiceError.BadRequest(validation.ErrorMessage!);
        }

        // Authorization for sharing:
        // - Orphan assets: Only system admins (they're the only ones who can access the orphans page)
        // - Assets in collections / Collections: Require Manager role on at least one collection
        var hasAccess = false;
        if (validation.IsOrphanAsset)
        {
            // Orphan asset: only system admins can share
            hasAccess = _currentUser.IsSystemAdmin;
        }
        else
        {
            foreach (var collectionId in validation.CollectionIdsToCheck)
            {
                if (await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Manager, ct))
                {
                    hasAccess = true;
                    break;
                }
            }
        }
        if (!hasAccess)
            return ServiceError.Forbidden("You don't have permission to share this resource");

        var result = await _shareService.CreateShareAsync(dto, userId, baseUrl, ct);
        if (result.IsError)
            return ServiceError.BadRequest(result.ErrorMessage!);
        return result.Response!;
    }

    public async Task<ServiceResult> RevokeShareAsync(Guid shareId, CancellationToken ct)
    {
        var share = await _shareRepo.GetByIdAsync(shareId, ct);
        if (share == null)
            return ServiceError.NotFound("Share not found");

        var userId = _currentUser.UserId;
        if (share.CreatedByUserId != userId)
            return ServiceError.Forbidden("You don't have permission to revoke this share");

        share.RevokedAt = DateTime.UtcNow;
        await _shareRepo.UpdateAsync(share, ct);

        await _audit.LogAsync("share.revoked", Constants.ScopeTypes.Share, shareId, userId,
            new() { ["scopeType"] = share.ScopeType, ["scopeId"] = share.ScopeId },
            ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<MessageResponse>> UpdateSharePasswordAsync(
        Guid shareId, string password, CancellationToken ct)
    {
        var share = await _shareRepo.GetByIdAsync(shareId, ct);
        if (share == null)
            return ServiceError.NotFound("Share not found");

        var userId = _currentUser.UserId;
        var isAdmin = _currentUser.IsSystemAdmin;
        if (share.CreatedByUserId != userId && !isAdmin)
            return ServiceError.Forbidden("You don't have permission to update this share");

        if (string.IsNullOrWhiteSpace(password))
            return ServiceError.BadRequest("Password cannot be empty");
        var pwError = InputValidation.ValidateSharePassword(password);
        if (pwError != null)
            return ServiceError.BadRequest(pwError);

        share.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        
        // Store encrypted password so admins can retrieve it later
        var protector = _dataProtection.CreateProtector(Constants.DataProtection.SharePasswordProtector);
        var protectedBytes = protector.Protect(System.Text.Encoding.UTF8.GetBytes(password));
        share.PasswordEncrypted = Convert.ToBase64String(protectedBytes);
        
        await _shareRepo.UpdateAsync(share, ct);

        await _audit.LogAsync("share.password_updated", Constants.ScopeTypes.Share, shareId, userId,
            new() { ["scopeType"] = share.ScopeType, ["scopeId"] = share.ScopeId },
            ct);

        return new MessageResponse("Password updated successfully");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<(Share? share, ServiceError? error)> ValidateAndGetShareAsync(
        string token, string? password, CancellationToken ct)
    {
        var tokenHash = ShareHelpers.ComputeTokenHash(token);
        var share = await _shareRepo.GetByTokenHashAsync(tokenHash, ct);
        if (share == null)
            return (null, ServiceError.NotFound("Share link not found or invalid"));

        var accessError = ShareHelpers.ValidateShareAccess(share.RevokedAt, share.ExpiresAt);
        if (accessError != null)
        {
            var error = accessError == Constants.ShareErrorCodes.Revoked
                ? ServiceError.ShareRevoked()
                : ServiceError.ShareExpired();
            return (null, error);
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
                // Log failed password attempt for security monitoring
                var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
                _logger.LogWarning("Failed share password attempt for token hash {TokenHashPrefix}... from IP {IP}",
                    tokenHash[..Math.Min(8, tokenHash.Length)], ip ?? "unknown");
                await _audit.LogAsync("share.password_failed", Constants.ScopeTypes.Share, share.Id, null,
                    new() { ["ip"] = ip ?? "unknown", ["tokenHashPrefix"] = tokenHash[..Math.Min(8, tokenHash.Length)] },
                    ct);
                return (null, new ServiceError(401, "UNAUTHORIZED", "Invalid password"));
            }
        }

        return (share, null);
    }

    /// <summary>
    /// Checks whether the given value is a valid, non-expired access token
    /// for the specified share (identified by its token hash).
    /// </summary>
    private bool IsValidAccessToken(string expectedTokenHash, string possibleAccessToken)
    {
        try
        {
            var protector = _dataProtection.CreateProtector(
                Constants.DataProtection.ShareAccessTokenProtector);
            var payload = protector.Unprotect(possibleAccessToken);
            var parts = payload.Split(':');

            // Expected format: "share-access:{tokenHash}:{expiryUnixSeconds}"
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
            // Decryption failed → not an access token, just a regular password
            return false;
        }
    }

    private static SharedAssetDto BuildSharedAssetDto(
        Asset asset, string token, Dictionary<string, bool> permissions, Guid? assetId = null)
    {
        // Strip GPS coordinates from shared metadata to protect location privacy
        // when assets are accessed by external share-link recipients (CWE-359).
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
            var asset = await _assetRepo.GetByIdAsync(share.ScopeId, ct);
            return asset != null
                ? (asset, null)
                : (null, ServiceError.NotFound("Asset not found"));
        }

        if (share.ScopeType == ShareScopeType.Collection)
        {
            if (!assetId.HasValue)
                return (null, ServiceError.BadRequest("assetId query parameter is required for collection shares"));

            var asset = await _assetRepo.GetByIdAsync(assetId.Value, ct);
            if (asset == null)
                return (null, ServiceError.NotFound("Asset not found in this shared collection"));

            var belongsToCollection = await _assetCollectionRepo.BelongsToCollectionAsync(assetId.Value, share.ScopeId, ct);
            if (!belongsToCollection)
                return (null, ServiceError.NotFound("Asset not found in this shared collection"));

            return (asset, null);
        }

        return (null, ServiceError.BadRequest("Invalid share scope type"));
    }

    private async Task<ServiceResult<string>> GetPresignedUrl(string objectKey, bool forceDownload, CancellationToken ct)
    {
        var url = await _minioAdapter.GetPresignedDownloadUrlAsync(
            _bucketName, objectKey, Constants.Limits.PresignedDownloadExpirySec, forceDownload, null, ct);
        return url;
    }
}
