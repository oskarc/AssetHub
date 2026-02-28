using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Read-only asset operations: queries, listing, presigned downloads.
/// </summary>
public sealed class AssetQueryService : IAssetQueryService
{
    private readonly IAssetRepository _assetRepo;
    private readonly IAssetCollectionRepository _assetCollectionRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IMinIOAdapter _minioAdapter;
    private readonly IAuditService _audit;
    private readonly CurrentUser _currentUser;
    private readonly string _bucketName;
    private readonly ILogger<AssetQueryService> _logger;

    public AssetQueryService(
        IAssetRepository assetRepo,
        IAssetCollectionRepository assetCollectionRepo,
        ICollectionRepository collectionRepo,
        ICollectionAuthorizationService authService,
        IMinIOAdapter minioAdapter,
        IAuditService audit,
        CurrentUser currentUser,
        IOptions<MinIOSettings> minioSettings,
        ILogger<AssetQueryService> logger)
    {
        _assetRepo = assetRepo;
        _assetCollectionRepo = assetCollectionRepo;
        _collectionRepo = collectionRepo;
        _authService = authService;
        _minioAdapter = minioAdapter;
        _audit = audit;
        _currentUser = currentUser;
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

    public async Task<ServiceResult<AllAssetsListResponse>> GetAllAssetsAsync(
        string? query, string? type, Guid? collectionId,
        string sortBy, int skip, int take, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        // System admins see all assets — skip collection access resolution entirely
        if (_currentUser.IsSystemAdmin)
        {
            var adminFilterIds = collectionId.HasValue ? new List<Guid> { collectionId.Value } : null;
            var (adminAssets, adminTotal) = await _assetRepo.SearchAllAsync(query, type, sortBy, skip, take, adminFilterIds, includeAllStatuses: true, ct);
            var adminDtos = adminAssets.Select(a => AssetMapper.ToDto(a, RoleHierarchy.Roles.Admin)).ToList();
            return new AllAssetsListResponse { Total = adminTotal, Items = adminDtos };
        }

        // Get all collections the user has access to (includes inherited access)
        var accessibleCollections = await _collectionRepo.GetAccessibleCollectionsAsync(userId, ct);
        var accessibleCollectionIds = accessibleCollections.Select(c => c.Id).ToList();

        // Batch-resolve roles for all accessible collections (single query, no N+1)
        var collectionRoles = (await _authService.GetUserRolesAsync(userId, accessibleCollectionIds, ct))
            .Where(kv => kv.Value != null)
            .ToDictionary(kv => kv.Key, kv => kv.Value!);

        // If a specific collection is requested, filter to just that one (if accessible)
        if (collectionId.HasValue)
        {
            if (!accessibleCollectionIds.Contains(collectionId.Value))
                return ServiceError.Forbidden();
            accessibleCollectionIds = [collectionId.Value];
        }

        // Non-admins are restricted to their accessible collections
        var (assets, total) = await _assetRepo.SearchAllAsync(query, type, sortBy, skip, take, accessibleCollectionIds, includeAllStatuses: false, ct);

        // Batch-load collection IDs for all returned assets (single query, no N+1)
        var assetIds = assets.Select(a => a.Id).ToList();
        var assetCollectionMap = await _assetCollectionRepo.GetCollectionIdsForAssetsAsync(assetIds, ct);

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

        return new AllAssetsListResponse { Total = total, Items = dtos };
    }

    public async Task<ServiceResult<AssetResponseDto>> GetAssetAsync(Guid id, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        if (_currentUser.IsSystemAdmin)
            return AssetMapper.ToDto(asset, RoleHierarchy.Roles.Admin);

        var linkedCollections = await _assetCollectionRepo.GetCollectionsForAssetAsync(id, ct);
        foreach (var collection in linkedCollections)
        {
            var role = await _authService.GetUserRoleAsync(_currentUser.UserId, collection.Id, ct);
            if (role != null)
                return AssetMapper.ToDto(asset, role);
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
            if (role == null)
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
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

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
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

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
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var sizeNormalized = size.ToLower();
        var isOriginal = sizeNormalized == "original";

        // Block all requests while upload is in progress (no files in storage yet)
        if (asset.Status == AssetStatus.Uploading)
        {
            return ServiceError.BadRequest("Asset is still being uploaded. Please wait for the upload to complete.");
        }

        // For forced downloads (not previews), provide helpful error messages if not ready
        if (forceDownload && asset.Status != AssetStatus.Ready && !isOriginal)
        {
            return asset.Status switch
            {
                AssetStatus.Processing => ServiceError.BadRequest("Asset is still being processed. Please try again in a few moments."),
                AssetStatus.Failed => ServiceError.BadRequest("Asset processing failed. Original file may still be available for download."),
                _ => ServiceError.BadRequest("Asset is not available for download.")
            };
        }

        string? objectKey = sizeNormalized switch
        {
            "original" => asset.OriginalObjectKey,
            "thumb" => asset.ThumbObjectKey,
            "medium" => !string.IsNullOrEmpty(asset.MediumObjectKey) ? asset.MediumObjectKey : asset.OriginalObjectKey,
            "poster" => asset.PosterObjectKey,
            _ => asset.OriginalObjectKey
        };

        if (string.IsNullOrEmpty(objectKey))
            return ServiceError.NotFound($"{size} rendition not available");

        // Build a friendly download filename from the asset title
        string? downloadFileName = null;
        if (forceDownload)
        {
            var ext = Path.GetExtension(objectKey);
            var prefix = size.ToLower() switch
            {
                "thumb" => "_thumb",
                "medium" => "_medium",
                "poster" => "_poster",
                _ => ""
            };
            downloadFileName = $"{asset.Title}{prefix}{ext}";
        }

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
            await _audit.LogAsync("asset.downloaded", "asset", id, _currentUser.UserId,
                new() { ["title"] = asset.Title, ["size"] = size },
                ct);
        }

        return presignedUrl;
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
}
