using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Command operations for assets: update, delete, collection membership.
/// For queries, see <see cref="AssetQueryService"/>.
/// For uploads, see <see cref="AssetUploadService"/>.
/// </summary>
public sealed class AssetService : IAssetService
{
    private readonly IAssetRepository _assetRepo;
    private readonly IAssetCollectionRepository _assetCollectionRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IAssetDeletionService _deletionService;
    private readonly IAuditService _audit;
    private readonly CurrentUser _currentUser;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AssetService> _logger;

    public AssetService(
        IAssetRepository assetRepo,
        IAssetCollectionRepository assetCollectionRepo,
        ICollectionAuthorizationService authService,
        IAssetDeletionService deletionService,
        IAuditService audit,
        CurrentUser currentUser,
        IConfiguration configuration,
        ILogger<AssetService> logger)
    {
        _assetRepo = assetRepo;
        _assetCollectionRepo = assetCollectionRepo;
        _authService = authService;
        _deletionService = deletionService;
        _audit = audit;
        _currentUser = currentUser;
        _configuration = configuration;
        _logger = logger;
    }

    private string BucketName => StorageConfig.GetBucketName(_configuration);

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
        if (dto.Tags != null)
        {
            if (dto.Tags.Any(t => t.Length > Constants.Limits.MaxTagLength))
                return ServiceError.BadRequest($"Each tag must be {Constants.Limits.MaxTagLength} characters or fewer");
            asset.Tags = dto.Tags;
        }
        if (dto.MetadataJson != null)
        {
            if (dto.MetadataJson.Count > Constants.Limits.MaxMetadataEntries)
                return ServiceError.BadRequest($"Metadata cannot exceed {Constants.Limits.MaxMetadataEntries} entries");
            if (dto.MetadataJson.Keys.Any(k => k.Length > Constants.Limits.MaxMetadataKeyLength))
                return ServiceError.BadRequest($"Each metadata key must be {Constants.Limits.MaxMetadataKeyLength} characters or fewer");
            if (dto.MetadataJson.Values.Any(v => v?.ToString()?.Length > Constants.Limits.MaxMetadataValueLength))
                return ServiceError.BadRequest($"Each metadata value must be {Constants.Limits.MaxMetadataValueLength} characters or fewer");
            asset.MetadataJson = dto.MetadataJson;
        }
        asset.UpdatedAt = DateTime.UtcNow;

        await _assetRepo.UpdateAsync(asset, ct);
        await _audit.LogAsync("asset.updated", "asset", id, _currentUser.UserId,
            new() { ["title"] = asset.Title, ["description"] = asset.Description ?? "", ["tags"] = string.Join(", ", asset.Tags ?? []) },
            ct);

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
                var canAccess = await _authService.CheckAccessAsync(userId, fromCollectionId.Value, RoleHierarchy.Roles.Manager, ct);
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
                    ct);
            }
            else
            {
                await _audit.LogAsync("asset.removed_from_collection", "asset", id, userId,
                    new() { ["title"] = asset.Title, ["collectionId"] = fromCollectionId.Value.ToString() },
                    ct);
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
                    ct);
                return ServiceResult.Success;
            }
        }

        // Full permanent delete
        await _deletionService.PermanentDeleteAsync(asset, BucketName, ct);
        await _audit.LogAsync("asset.deleted", "asset", id, userId,
            new() { ["title"] = asset.Title }, ct);

        return ServiceResult.Success;
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

        await _audit.LogAsync("asset.added_to_collection", "asset", assetId, userId,
            new() { ["title"] = asset.Title, ["collectionId"] = collectionId.ToString() }, ct);

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
            var canAccess = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Manager, ct);
            if (!canAccess)
                return ServiceError.Forbidden("You don't have permission to manage this asset in this collection");
        }

        var (removed, permanentlyDeleted) = await _deletionService.RemoveFromCollectionAsync(asset, collectionId, BucketName, ct);
        if (!removed)
            return ServiceError.NotFound("Asset is not linked to this collection");

        if (permanentlyDeleted)
        {
            await _audit.LogAsync("asset.deleted", "asset", assetId, userId,
                new() { ["title"] = asset.Title, ["reason"] = "orphaned" }, ct);
        }
        else
        {
            await _audit.LogAsync("asset.removed_from_collection", "asset", assetId, userId,
                new() { ["title"] = asset.Title, ["collectionId"] = collectionId.ToString() }, ct);
        }

        return ServiceResult.Success;
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
