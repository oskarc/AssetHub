using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

/// <summary>
/// Orchestrates asset operations: queries, uploads, updates, deletions,
/// multi-collection management, presigned uploads, and renditions.
/// </summary>
public class AssetService : IAssetService
{
    private readonly IAssetRepository _assetRepo;
    private readonly IAssetCollectionRepository _assetCollectionRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IMinIOAdapter _minioAdapter;
    private readonly IMediaProcessingService _mediaProcessing;
    private readonly IAssetDeletionService _deletionService;
    private readonly IAuditService _audit;
    private readonly IConfiguration _configuration;
    private readonly CurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AssetService> _logger;

    public AssetService(
        IAssetRepository assetRepo,
        IAssetCollectionRepository assetCollectionRepo,
        ICollectionRepository collectionRepo,
        ICollectionAuthorizationService authService,
        IMinIOAdapter minioAdapter,
        IMediaProcessingService mediaProcessing,
        IAssetDeletionService deletionService,
        IAuditService audit,
        IConfiguration configuration,
        CurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AssetService> logger)
    {
        _assetRepo = assetRepo;
        _assetCollectionRepo = assetCollectionRepo;
        _collectionRepo = collectionRepo;
        _authService = authService;
        _minioAdapter = minioAdapter;
        _mediaProcessing = mediaProcessing;
        _deletionService = deletionService;
        _audit = audit;
        _configuration = configuration;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private string BucketName => StorageConfig.GetBucketName(_configuration);
    private HttpContext? HttpContext => _httpContextAccessor.HttpContext;

    // ── Queries ──────────────────────────────────────────────────────────────

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

        // Build role map using the authorization service (respects inheritance)
        var collectionRoles = new Dictionary<Guid, string>();
        foreach (var coll in accessibleCollections)
        {
            var role = await _authService.GetUserRoleAsync(userId, coll.Id, ct);
            if (role != null)
                collectionRoles[coll.Id] = role;
        }

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

    // ── Commands ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AssetUploadResult>> UploadAsync(
        Stream fileStream, string fileName, string contentType, long fileSize,
        Guid collectionId, string title, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var canContribute = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
        if (!canContribute)
            return ServiceError.Forbidden();

        if (fileSize == 0)
            return ServiceError.BadRequest("File is required");

        var sizeError = ValidateFileSize(fileSize);
        if (sizeError != null) return sizeError;

        var asset = CreateAssetEntity(fileName, contentType, fileSize, userId, Asset.StatusProcessing);
        if (!string.IsNullOrEmpty(title))
            asset.Title = title;

        await _minioAdapter.UploadAsync(BucketName, asset.OriginalObjectKey, fileStream, contentType, ct);
        await _assetRepo.CreateAsync(asset, ct);
        await _assetCollectionRepo.AddToCollectionAsync(asset.Id, collectionId, userId, ct);

        await _audit.LogAsync("asset.created", "asset", asset.Id, userId,
            new() { ["title"] = title ?? "", ["collectionId"] = collectionId, ["contentType"] = contentType },
            HttpContext, ct);

        var jobId = await _mediaProcessing.ScheduleProcessingAsync(asset.Id, asset.AssetType, asset.OriginalObjectKey, ct);

        return new AssetUploadResult
        {
            Id = asset.Id,
            Status = Asset.StatusProcessing,
            JobId = jobId,
            Message = "Asset uploaded. Processing in progress."
        };
    }

    public async Task<ServiceResult<AssetResponseDto>> UpdateAsync(
        Guid id, UpdateAssetDto dto, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Contributor, ct))
            return ServiceError.Forbidden();

        if (dto.Title != null) asset.Title = dto.Title;
        if (dto.Description != null) asset.Description = dto.Description;
        if (dto.Copyright != null) asset.Copyright = dto.Copyright;
        if (dto.Tags != null) asset.Tags = dto.Tags;
        if (dto.MetadataJson != null) asset.MetadataJson = dto.MetadataJson;
        asset.UpdatedAt = DateTime.UtcNow;

        await _assetRepo.UpdateAsync(asset, ct);
        await _audit.LogAsync("asset.updated", "asset", id, _currentUser.UserId, httpContext: HttpContext, ct: ct);

        return AssetMapper.ToDto(asset);
    }

    public async Task<ServiceResult> DeleteAsync(
        Guid id, Guid? fromCollectionId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        // "Remove from this collection" mode — backend decides if it becomes a permanent delete
        if (fromCollectionId.HasValue)
        {
            if (!_currentUser.IsSystemAdmin)
            {
                var canAccess = await _authService.CheckAccessAsync(userId, fromCollectionId.Value, RoleHierarchy.Roles.Contributor, ct);
                if (!canAccess)
                    return ServiceError.Forbidden();
            }

            var (removed, permanentlyDeleted) = await _deletionService.RemoveFromCollectionAsync(asset, fromCollectionId.Value, BucketName, ct);
            if (!removed)
                return ServiceError.NotFound("Asset is not linked to this collection");

            if (permanentlyDeleted)
            {
                await _audit.LogAsync("asset.deleted", "asset", id, userId,
                    new() { ["title"] = asset.Title, ["reason"] = "last_collection" },
                    HttpContext, ct);
            }
            else
            {
                await _audit.LogAsync("asset.removed_from_collection", "asset", id, userId,
                    new() { ["title"] = asset.Title, ["collectionId"] = fromCollectionId.Value.ToString() },
                    HttpContext, ct);
            }
            return ServiceResult.Success;
        }

        // Full permanent delete (no specific collection context)
        var assetCollections = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(id, ct);

        if (!_currentUser.IsSystemAdmin)
        {
            var authorizedCollectionIds = await _authService.FilterAccessibleAsync(
                userId, assetCollections, RoleHierarchy.Roles.Manager, ct);
            if (authorizedCollectionIds.Count == 0)
                return ServiceError.Forbidden();

            var unauthorizedRemain = assetCollections.Except(authorizedCollectionIds).Any();
            if (unauthorizedRemain)
            {
                // User can only remove from the collections they manage
                foreach (var collId in authorizedCollectionIds)
                    await _assetCollectionRepo.RemoveFromCollectionAsync(id, collId, ct);

                await _audit.LogAsync("asset.removed_from_collections", "asset", id, userId,
                    new() { ["title"] = asset.Title, ["count"] = authorizedCollectionIds.Count.ToString() },
                    HttpContext, ct);
                return ServiceResult.Success;
            }
        }

        // Full permanent delete
        await _deletionService.PermanentDeleteAsync(asset, BucketName, ct);
        await _audit.LogAsync("asset.deleted", "asset", id, userId,
            new() { ["title"] = asset.Title }, HttpContext, ct);

        return ServiceResult.Success;
    }

    // ── Presigned Upload ─────────────────────────────────────────────────────

    public async Task<ServiceResult<InitUploadResponse>> InitUploadAsync(
        InitUploadRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var sizeError = ValidateFileSize(request.FileSize);
        if (sizeError != null) return sizeError;

        if (request.CollectionId.HasValue)
        {
            var canContribute = await _authService.CheckAccessAsync(userId, request.CollectionId.Value, RoleHierarchy.Roles.Contributor, ct);
            if (!canContribute)
                return ServiceError.Forbidden();
        }
        else
        {
            // Standalone upload (no collection) requires system admin
            if (!_currentUser.IsSystemAdmin)
                return ServiceError.Forbidden();
        }

        var asset = CreateAssetEntity(request.FileName, request.ContentType, request.FileSize, userId, Asset.StatusUploading);
        if (!string.IsNullOrEmpty(request.Title))
            asset.Title = request.Title;

        await _assetRepo.CreateAsync(asset, ct);

        if (request.CollectionId.HasValue)
            await _assetCollectionRepo.AddToCollectionAsync(asset.Id, request.CollectionId.Value, userId, ct);

        var presignedUrl = await _minioAdapter.GetPresignedUploadUrlAsync(
            BucketName, asset.OriginalObjectKey, Constants.Limits.PresignedUploadExpirySec, ct);

        return new InitUploadResponse
        {
            AssetId = asset.Id,
            ObjectKey = asset.OriginalObjectKey,
            UploadUrl = presignedUrl,
            ExpiresInSeconds = Constants.Limits.PresignedUploadExpirySec
        };
    }

    public async Task<ServiceResult<AssetUploadResult>> ConfirmUploadAsync(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        if (asset.CreatedByUserId != userId)
            return ServiceError.Forbidden();

        if (asset.Status != Asset.StatusUploading)
            return ServiceError.BadRequest("Asset is not in uploading state");

        var stat = await _minioAdapter.StatObjectAsync(BucketName, asset.OriginalObjectKey, ct);
        if (stat == null)
            return ServiceError.BadRequest("File not found in storage. Upload may have failed or expired.");

        asset.SizeBytes = stat.Size;
        asset.Status = Asset.StatusProcessing;
        asset.UpdatedAt = DateTime.UtcNow;
        await _assetRepo.UpdateAsync(asset, ct);

        var jobId = await _mediaProcessing.ScheduleProcessingAsync(asset.Id, asset.AssetType, asset.OriginalObjectKey, ct);

        return new AssetUploadResult
        {
            Id = asset.Id,
            Status = Asset.StatusProcessing,
            SizeBytes = stat.Size,
            JobId = jobId,
            Message = "Upload confirmed. Processing in progress."
        };
    }

    // ── Multi-Collection ─────────────────────────────────────────────────────

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

    public async Task<ServiceResult<AssetAddedToCollectionResponse>> AddToCollectionAsync(
        Guid assetId, Guid collectionId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var asset = await _assetRepo.GetByIdAsync(assetId, ct);
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        if (!_currentUser.IsSystemAdmin)
        {
            var assetCollections = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
            bool canAccessAsset = false;

            foreach (var acId in assetCollections)
            {
                if (await _authService.CheckAccessAsync(userId, acId, RoleHierarchy.Roles.Contributor, ct))
                {
                    canAccessAsset = true;
                    break;
                }
            }

            if (!canAccessAsset && assetCollections.Count == 0)
                canAccessAsset = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);

            if (!canAccessAsset)
                return ServiceError.Forbidden("You don't have permission to manage this asset");

            var canAccessTarget = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
            if (!canAccessTarget)
                return ServiceError.Forbidden("You don't have permission to add assets to this collection");
        }

        var result = await _assetCollectionRepo.AddToCollectionAsync(assetId, collectionId, userId, ct);
        if (result == null)
            return ServiceError.BadRequest("Asset is already linked to this collection or collection not found");

        return new AssetAddedToCollectionResponse
        {
            AssetId = assetId,
            CollectionId = collectionId,
            AddedAt = result.AddedAt,
            Message = "Asset added to collection"
        };
    }

    public async Task<ServiceResult> RemoveFromCollectionAsync(
        Guid assetId, Guid collectionId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var asset = await _assetRepo.GetByIdAsync(assetId, ct);
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        if (!_currentUser.IsSystemAdmin)
        {
            var canAccess = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
            if (!canAccess)
                return ServiceError.Forbidden("You don't have permission to manage this asset in this collection");
        }

        var (removed, permanentlyDeleted) = await _deletionService.RemoveFromCollectionAsync(asset, collectionId, BucketName, ct);
        if (!removed)
            return ServiceError.NotFound("Asset is not linked to this collection");

        if (permanentlyDeleted)
        {
            await _audit.LogAsync("asset.deleted", "asset", assetId, userId,
                new() { ["title"] = asset.Title, ["reason"] = "orphaned" }, HttpContext, ct);
        }
        else
        {
            await _audit.LogAsync("asset.removed_from_collection", "asset", assetId, userId,
                new() { ["title"] = asset.Title, ["collectionId"] = collectionId.ToString() }, HttpContext, ct);
        }

        return ServiceResult.Success;
    }

    // ── Renditions ───────────────────────────────────────────────────────────

    public async Task<ServiceResult<string>> GetRenditionUrlAsync(
        Guid id, string size, bool forceDownload, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset == null)
            return ServiceError.NotFound("Asset not found");

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        string? objectKey = size.ToLower() switch
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

        var presignedUrl = await _minioAdapter.GetPresignedDownloadUrlAsync(
            BucketName, objectKey, Constants.Limits.PresignedDownloadExpirySec, forceDownload, downloadFileName, ct);

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

    private ServiceError? ValidateFileSize(long fileSize)
    {
        var maxSizeMb = _configuration.GetValue("App:MaxUploadSizeMb", Constants.Limits.DefaultMaxUploadSizeMb);
        var maxSizeBytes = (long)maxSizeMb * 1024 * 1024;
        return fileSize > maxSizeBytes
            ? ServiceError.BadRequest($"File size exceeds the maximum allowed size of {maxSizeMb} MB")
            : null;
    }

    private static Asset CreateAssetEntity(
        string fileName, string contentType, long sizeBytes, string userId, string status)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        var assetType = AssetTypeHelper.DetermineAssetType(contentType, extension);
        var assetId = Guid.NewGuid();
        var objectKey = $"originals/{assetId}-{Path.GetFileName(fileName)}";

        return new Asset
        {
            Id = assetId,
            AssetType = assetType,
            Status = status,
            Title = Path.GetFileNameWithoutExtension(fileName),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            OriginalObjectKey = objectKey,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
