using System.Text.Json;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Watermarking;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups repository dependencies for <see cref="AssetQueryService"/>
/// to keep the constructor parameter count manageable.
/// </summary>
public sealed record AssetQueryRepositories(
    IAssetRepository AssetRepo,
    IAssetCollectionRepository AssetCollectionRepo,
    ICollectionRepository CollectionRepo);

/// <summary>
/// Read-only asset operations: queries, listing, presigned downloads.
/// </summary>
public sealed class AssetQueryService : IAssetQueryService
{
    private const string AssetNotFound = "Asset not found";

    private readonly IAssetRepository _assetRepo;
    private readonly IAssetCollectionRepository _assetCollectionRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IMinIOAdapter _minioAdapter;
    private readonly IAuditService _audit;
    private readonly CurrentUser _currentUser;
    private readonly IWatermarkService _watermarkService;
    private readonly string _bucketName;
    private readonly ILogger<AssetQueryService> _logger;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
        Justification = "Composition root for asset queries: repos, auth, MinIO, audit, current user, watermark dispatch, settings, logger are all inherent dependencies.")]
    public AssetQueryService(
        AssetQueryRepositories repos,
        ICollectionAuthorizationService authService,
        IMinIOAdapter minioAdapter,
        IAuditService audit,
        CurrentUser currentUser,
        IWatermarkService watermarkService,
        IOptions<MinIOSettings> minioSettings,
        ILogger<AssetQueryService> logger)
    {
        _assetRepo = repos.AssetRepo;
        _assetCollectionRepo = repos.AssetCollectionRepo;
        _authService = authService;
        _minioAdapter = minioAdapter;
        _audit = audit;
        _currentUser = currentUser;
        _watermarkService = watermarkService;
        _bucketName = minioSettings.Value.BucketName;
        _logger = logger;
    }


    public async Task<ServiceResult<List<AssetResponseDto>>> GetAssetsByStatusAsync(
        string status, int skip, int take, CancellationToken ct)
    {
        var assets = await _assetRepo.GetByStatusAsync(status, skip, take, ct);
        var dtos = assets.Select(a => AssetMapper.ToDto(a)).ToList();
        return dtos;
    }


    public async Task<ServiceResult<AssetResponseDto>> GetAssetAsync(Guid id, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFound);

        var derivativeCount = await _assetRepo.CountDerivativesAsync(id, ct);

        if (_currentUser.IsSystemAdmin)
            return AssetMapper.ToDto(asset, RoleHierarchy.Roles.Admin, derivativeCount: derivativeCount, includeEditDocument: true);

        var linkedCollections = await _assetCollectionRepo.GetCollectionsForAssetAsync(id, ct);
        var collectionIds = linkedCollections.Select(c => c.Id).ToList();
        var roleMap = await _authService.GetUserRolesAsync(_currentUser.UserId, collectionIds, ct);
        foreach (var collection in linkedCollections)
        {
            if (roleMap.TryGetValue(collection.Id, out var role) && role is not null)
                return AssetMapper.ToDto(asset, role, derivativeCount: derivativeCount, includeEditDocument: true);
        }

        return ServiceError.Forbidden();
    }

    public async Task<ServiceResult<AssetListResponse>> GetAssetsByCollectionAsync(
        Guid collectionId, string? query, string? type,
        string sortBy, int skip, int take, CancellationToken ct)
    {
        string userRole;
        if (_currentUser.IsSystemAdmin)
        {
            userRole = RoleHierarchy.Roles.Admin;
        }
        else
        {
            var role = await _authService.GetUserRoleAsync(_currentUser.UserId, collectionId, ct);
            if (role is null)
                return ServiceError.Forbidden();
            userRole = role;
        }

        var (assets, total) = await _assetRepo.SearchAsync(collectionId, query, type, sortBy, skip, take, ct);
        var dtos = assets.Select(a => AssetMapper.ToDto(a, userRole)).ToList();
        return new AssetListResponse
        {
            CollectionId = collectionId,
            Total = total,
            Items = dtos
        };
    }

    public async Task<ServiceResult<AssetDeletionContextDto>> GetDeletionContextAsync(
        Guid id, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFound);

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var collectionIds = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);

        bool canDeleteAll = _currentUser.IsSystemAdmin;
        if (!_currentUser.IsSystemAdmin)
        {
            var accessible = await _authService.FilterAccessibleAsync(
                _currentUser.UserId, collectionIds, RoleHierarchy.Roles.Manager, ct);
            canDeleteAll = accessible.Count == collectionIds.Count;
        }

        return new AssetDeletionContextDto
        {
            CollectionCount = collectionIds.Count,
            CanDeletePermanently = canDeleteAll
        };
    }

    public async Task<ServiceResult<IEnumerable<AssetCollectionDto>>> GetAssetCollectionsAsync(
        Guid id, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFound);

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var collections = await _assetCollectionRepo.GetCollectionsForAssetAsync(id, ct);
        var result = collections.Select(c => new AssetCollectionDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description
        });

        return new ServiceResult<IEnumerable<AssetCollectionDto>> { Value = result };
    }

    public async Task<ServiceResult<string>> GetRenditionUrlAsync(
        Guid id, string size, bool forceDownload, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFound);

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var statusError = ValidateAssetStatus(asset, size, forceDownload);
        if (statusError is not null)
            return statusError;

        var objectKey = ResolveObjectKey(asset, size);
        if (string.IsNullOrEmpty(objectKey))
            return ServiceError.NotFound($"{size} rendition not available");

        var downloadFileName = forceDownload ? BuildDownloadFileName(asset.Title, size, objectKey) : null;

        string presignedUrl;
        try
        {
            presignedUrl = await _minioAdapter.GetPresignedDownloadUrlAsync(
                _bucketName, objectKey, Constants.Limits.PresignedDownloadExpirySec, forceDownload, downloadFileName, ct);
        }
        catch (StorageException ex)
        {
            _logger.LogError(ex, "Failed to generate presigned download URL for asset {AssetId}", id);
            return ServiceError.Server(ex.Message);
        }

        if (forceDownload)
        {
            await _audit.LogAsync("asset.downloaded", Constants.ScopeTypes.Asset, id, _currentUser.UserId,
                new() { ["title"] = asset.Title, ["size"] = size },
                ct);
        }

        return presignedUrl;
    }

    public async Task<ServiceResult<RenditionDownloadResult>> ResolveRenditionDownloadAsync(
        Guid id, string size, bool forceDownload, Guid? shareId, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFound);

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var statusError = ValidateAssetStatus(asset, size, forceDownload);
        if (statusError is not null)
            return statusError;

        var objectKey = ResolveObjectKey(asset, size);
        if (string.IsNullOrEmpty(objectKey))
            return ServiceError.NotFound($"{size} rendition not available");

        var downloadFileName = forceDownload ? BuildDownloadFileName(asset.Title, size, objectKey) : null;

        // T5-WMK-01 — precedence-aware dispatch. Un-watermarked downloads keep the
        // CDN-fast 302 redirect; watermarked downloads stream through the API so
        // both layers (asset-fingerprint + recipient-fingerprint) can be embedded.
        var effectiveResult = await _watermarkService.IsWatermarkingEffectiveAsync(id, shareId, ct);
        var watermarkOn = effectiveResult.IsSuccess && effectiveResult.Value;

        if (!watermarkOn)
        {
            return await BuildPresignedRedirectAsync(asset, id, size, objectKey, forceDownload, downloadFileName, ct);
        }

        return await BuildWatermarkedStreamAsync(asset, id, size, objectKey, shareId, forceDownload, downloadFileName, ct);
    }

    private async Task<ServiceResult<RenditionDownloadResult>> BuildPresignedRedirectAsync(
        Asset asset, Guid id, string size, string objectKey, bool forceDownload,
        string? downloadFileName, CancellationToken ct)
    {
        string presignedUrl;
        try
        {
            presignedUrl = await _minioAdapter.GetPresignedDownloadUrlAsync(
                _bucketName, objectKey, Constants.Limits.PresignedDownloadExpirySec,
                forceDownload, downloadFileName, ct);
        }
        catch (StorageException ex)
        {
            _logger.LogError(ex, "Failed to generate presigned download URL for asset {AssetId}", id);
            return ServiceError.Server(ex.Message);
        }

        if (forceDownload)
        {
            await _audit.LogAsync("asset.downloaded", Constants.ScopeTypes.Asset, id, _currentUser.UserId,
                new() { ["title"] = asset.Title, ["size"] = size }, ct);
        }

        return new RenditionDownloadResult { PresignedUrl = presignedUrl };
    }

    private async Task<ServiceResult<RenditionDownloadResult>> BuildWatermarkedStreamAsync(
        Asset asset, Guid id, string size, string objectKey, Guid? shareId, bool forceDownload,
        string? downloadFileName, CancellationToken ct)
    {
        Stream sourceStream;
        try
        {
            sourceStream = await _minioAdapter.DownloadAsync(_bucketName, objectKey, ct);
        }
        catch (StorageException ex)
        {
            _logger.LogError(ex, "Failed to download asset {AssetId} for watermarked stream", id);
            return ServiceError.Server(ex.Message);
        }

        var watermarkContext = new WatermarkContext
        {
            AssetId = id,
            DownloadPath = MapSizeToDownloadPath(size),
            ShareId = shareId,
            RecipientUserId = _currentUser.UserId,
            RecipientEmail = null
        };

        var watermarkResult = await _watermarkService.WatermarkForDownloadAsync(sourceStream, watermarkContext, ct);
        if (!watermarkResult.IsSuccess)
        {
            await sourceStream.DisposeAsync();
            return watermarkResult.Error!;
        }

        if (forceDownload)
        {
            await _audit.LogAsync("asset.downloaded", Constants.ScopeTypes.Asset, id, _currentUser.UserId,
                new() { ["title"] = asset.Title, ["size"] = size, ["watermarked"] = true }, ct);
        }

        var contentType = ResolveRenditionContentType(asset, size);

        return new RenditionDownloadResult
        {
            Stream = new RenditionStreamPayload
            {
                Content = watermarkResult.Value!.Output,
                ContentType = contentType,
                DownloadFileName = downloadFileName,
                ForceDownload = forceDownload
            }
        };
    }

    private static string MapSizeToDownloadPath(string size) => size.ToLower() switch
    {
        "original" => "raw",
        "thumb" => "thumb",
        "medium" => "medium",
        "poster" => "poster",
        _ => size.ToLower()
    };

    /// <summary>
    /// Content type for the rendition stream after watermarking. The watermark embedder
    /// re-encodes as PNG (lossless preserves the watermark bits), so all watermarked
    /// renditions are served as <c>image/png</c> — even if the source was JPEG. This
    /// is a deliberate v1 simplification; a future revision can add format-preserving
    /// embed paths for JPEG → JPEG round-trip.
    /// </summary>
    private static string ResolveRenditionContentType(Asset asset, string size)
        => "image/png";

    private static ServiceError? ValidateAssetStatus(Asset asset, string size, bool forceDownload)
    {
        if (asset.Status == AssetStatus.Uploading)
            return ServiceError.BadRequest("Asset is still being uploaded. Please wait for the upload to complete.");

        var isOriginal = size.Equals("original", StringComparison.OrdinalIgnoreCase);
        if (!forceDownload || asset.Status == AssetStatus.Ready || isOriginal)
            return null;

        return asset.Status switch
        {
            AssetStatus.Processing => ServiceError.BadRequest("Asset is still being processed. Please try again in a few moments."),
            AssetStatus.Failed => ServiceError.BadRequest("Asset processing failed. Original file may still be available for download."),
            _ => ServiceError.BadRequest("Asset is not available for download.")
        };
    }

    private static string? ResolveObjectKey(Asset asset, string size)
    {
        return size.ToLower() switch
        {
            "original" => asset.OriginalObjectKey,
            "thumb" => asset.ThumbObjectKey,
            "medium" => !string.IsNullOrEmpty(asset.MediumObjectKey) ? asset.MediumObjectKey : asset.OriginalObjectKey,
            "poster" => asset.PosterObjectKey,
            _ => asset.OriginalObjectKey
        };
    }

    private static string BuildDownloadFileName(string title, string size, string objectKey)
    {
        var ext = Path.GetExtension(objectKey);
        var prefix = size.ToLower() switch
        {
            "thumb" => "_thumb",
            "medium" => "_medium",
            "poster" => "_poster",
            _ => ""
        };
        return $"{title}{prefix}{ext}";
    }

    public async Task<ServiceResult<List<AssetDerivativeDto>>> GetDerivativesAsync(Guid id, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFound);

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var derivatives = await _assetRepo.GetDerivativesAsync(id, ct);
        var dtos = derivatives.Select(d => new AssetDerivativeDto
        {
            Id = d.Id,
            Title = d.Title,
            Status = d.Status.ToDbString(),
            ContentType = d.ContentType,
            SizeBytes = d.SizeBytes,
            ThumbObjectKey = d.ThumbObjectKey,
            Width = TryGetIntFromMetadata(d.MetadataJson, "width"),
            Height = TryGetIntFromMetadata(d.MetadataJson, "height")
        }).ToList();

        return dtos;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<bool> CanAccessAssetAsync(
        Guid assetId, string requiredRole, CancellationToken ct)
    {
        if (_currentUser.IsSystemAdmin)
            return true;

        var collections = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        var accessible = await _authService.FilterAccessibleAsync(_currentUser.UserId, collections, requiredRole, ct);
        return accessible.Count > 0;
    }

    /// <summary>
    /// Extracts an int from MetadataJson, handling both JsonElement (from DB) and string (from in-memory creation).
    /// </summary>
    private static int? TryGetIntFromMetadata(Dictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
            return null;

        if (value is JsonElement el)
            return el.TryGetInt32(out var intVal) ? intVal : null;

        if (value is int i)
            return i;

        if (value is string s && int.TryParse(s, out var parsed))
            return parsed;

        return null;
    }
}
