using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups repository dependencies for <see cref="AssetService"/>
/// to keep the constructor parameter count manageable.
/// </summary>
public sealed record AssetServiceRepositories(
    IAssetRepository AssetRepo,
    IAssetCollectionRepository AssetCollectionRepo,
    ICollectionRepository CollectionRepo);

/// <summary>
/// Command operations for assets: update, delete, collection membership.
/// For queries, see <see cref="AssetQueryService"/>.
/// For uploads, see <see cref="AssetUploadService"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root: pre-grouped repos record + auth + deletion + audit + UnitOfWork + cache + scoped CurrentUser + IOptions. Bundling further would obscure intent.")]
public sealed class AssetService : IAssetService
{
    private readonly IAssetRepository _assetRepo;
    private readonly IAssetCollectionRepository _assetCollectionRepo;
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IAssetDeletionService _deletionService;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _uow;
    private readonly HybridCache _cache;
    private readonly CurrentUser _currentUser;
    private readonly string _bucketName;
    private const string AssetNotFoundMessage = "Asset not found";
    private const string AuditKeyTitle = "title";

    public AssetService(
        AssetServiceRepositories repos,
        ICollectionAuthorizationService authService,
        IAssetDeletionService deletionService,
        IAuditService audit,
        IUnitOfWork uow,
        HybridCache cache,
        CurrentUser currentUser,
        IOptions<MinIOSettings> minioSettings)
    {
        _assetRepo = repos.AssetRepo;
        _assetCollectionRepo = repos.AssetCollectionRepo;
        _collectionRepo = repos.CollectionRepo;
        _authService = authService;
        _deletionService = deletionService;
        _audit = audit;
        _uow = uow;
        _cache = cache;
        _currentUser = currentUser;
        _bucketName = minioSettings.Value.BucketName;
    }


    public async Task<ServiceResult<AssetResponseDto>> UpdateAsync(
        Guid id, UpdateAssetDto dto, CancellationToken ct)
    {
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFoundMessage);

        if (!await CanAccessAssetAsync(id, RoleHierarchy.Roles.Contributor, ct))
            return ServiceError.Forbidden();

        var validationError = ApplyFieldUpdates(asset, dto);
        if (validationError is not null)
            return validationError;

        asset.UpdatedAt = DateTime.UtcNow;

        // Update + audit atomic (A-4).
        await _uow.ExecuteAsync(async tct =>
        {
            await _assetRepo.UpdateAsync(asset, tct);
            await _audit.LogAsync("asset.updated", Constants.ScopeTypes.Asset, id, _currentUser.UserId,
                new() { [AuditKeyTitle] = asset.Title, ["description"] = asset.Description ?? "", ["tags"] = string.Join(", ", asset.Tags ?? []) },
                tct);
        }, ct);

        var userRole = await GetUserRoleForAssetAsync(id, ct);
        return AssetMapper.ToDto(asset, userRole);
    }

    private static ServiceError? ApplyFieldUpdates(Domain.Entities.Asset asset, UpdateAssetDto dto)
    {
        return ApplyTitle(asset, dto)
            ?? ApplyDescription(asset, dto)
            ?? ApplyCopyright(asset, dto)
            ?? ApplyTags(asset, dto)
            ?? ApplyMetadata(asset, dto);
    }

    private static ServiceError? ApplyTitle(Domain.Entities.Asset asset, UpdateAssetDto dto)
    {
        if (dto.Title is null) return null;
        var error = InputValidation.ValidateAssetTitle(dto.Title);
        if (error is not null) return ServiceError.BadRequest(error);
        asset.Title = dto.Title;
        return null;
    }

    private static ServiceError? ApplyDescription(Domain.Entities.Asset asset, UpdateAssetDto dto)
    {
        if (dto.Description is null) return null;
        var desc = InputValidation.NormalizeToNull(dto.Description);
        if (desc is not null && desc.Length > 2000)
            return ServiceError.BadRequest("Description must be 2000 characters or fewer");
        asset.Description = desc;
        return null;
    }

    private static ServiceError? ApplyCopyright(Domain.Entities.Asset asset, UpdateAssetDto dto)
    {
        if (dto.Copyright is null) return null;
        var copyright = InputValidation.NormalizeToNull(dto.Copyright);
        if (copyright is not null && copyright.Length > 500)
            return ServiceError.BadRequest("Copyright must be 500 characters or fewer");
        asset.Copyright = copyright;
        return null;
    }

    private static ServiceError? ApplyTags(Domain.Entities.Asset asset, UpdateAssetDto dto)
    {
        if (dto.Tags is null) return null;
        if (dto.Tags.Count > Constants.Limits.MaxTagsPerAsset)
            return ServiceError.BadRequest($"Cannot have more than {Constants.Limits.MaxTagsPerAsset} tags");
        if (dto.Tags.Any(t => t.Length > Constants.Limits.MaxTagLength))
            return ServiceError.BadRequest($"Each tag must be {Constants.Limits.MaxTagLength} characters or fewer");
        asset.Tags = dto.Tags;
        return null;
    }

    private static ServiceError? ApplyMetadata(Domain.Entities.Asset asset, UpdateAssetDto dto)
    {
        if (dto.MetadataJson is null) return null;
        if (dto.MetadataJson.Count > Constants.Limits.MaxMetadataEntries)
            return ServiceError.BadRequest($"Metadata cannot exceed {Constants.Limits.MaxMetadataEntries} entries");
        if (dto.MetadataJson.Keys.Any(k => k.Length > Constants.Limits.MaxMetadataKeyLength))
            return ServiceError.BadRequest($"Each metadata key must be {Constants.Limits.MaxMetadataKeyLength} characters or fewer");
        if (dto.MetadataJson.Values.Any(v => v?.ToString()?.Length > Constants.Limits.MaxMetadataValueLength))
            return ServiceError.BadRequest($"Each metadata value must be {Constants.Limits.MaxMetadataValueLength} characters or fewer");
        asset.MetadataJson = dto.MetadataJson;
        return null;
    }

    public async Task<ServiceResult> DeleteAsync(
        Guid id, Guid? fromCollectionId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var asset = await _assetRepo.GetByIdAsync(id, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFoundMessage);

        return fromCollectionId.HasValue
            ? await DeleteFromCollectionAsync(asset, fromCollectionId.Value, userId, ct)
            : await SoftDeleteFullAsync(asset, userId, ct);
    }

    private async Task<ServiceResult> DeleteFromCollectionAsync(
        Domain.Entities.Asset asset, Guid fromCollectionId, string userId, CancellationToken ct)
    {
        if (!_currentUser.IsSystemAdmin)
        {
            var canAccess = await _authService.CheckAccessAsync(userId, fromCollectionId, RoleHierarchy.Roles.Manager, ct);
            if (!canAccess)
                return ServiceError.Forbidden();
        }

        // Unlink (and soft-delete if last collection) + audit atomic (A-4).
        // The deletion service performs only DB mutations on this path —
        // MinIO purge is a later worker.
        var (removed, softDeleted) = await _uow.ExecuteAsync(async tct =>
        {
            var outcome = await _deletionService.RemoveFromCollectionAsync(asset, fromCollectionId, userId, _bucketName, tct);
            if (!outcome.Removed) return outcome;

            var auditEvent = outcome.SoftDeleted ? "asset.deleted" : "asset.removed_from_collection";
            var auditDetails = outcome.SoftDeleted
                ? new Dictionary<string, object> { [AuditKeyTitle] = asset.Title, ["reason"] = "last_collection" }
                : new Dictionary<string, object> { [AuditKeyTitle] = asset.Title, ["collectionId"] = fromCollectionId.ToString() };
            await _audit.LogAsync(auditEvent, Constants.ScopeTypes.Asset, asset.Id, userId, auditDetails, tct);
            return outcome;
        }, ct);

        if (!removed)
            return ServiceError.NotFound("Asset is not linked to this collection");

        if (softDeleted)
            await _cache.RemoveByTagAsync(CacheKeys.Tags.Dashboard, ct);

        return ServiceResult.Success;
    }

    private async Task<ServiceResult> SoftDeleteFullAsync(
        Domain.Entities.Asset asset, string userId, CancellationToken ct)
    {
        // Full delete (no specific collection context) — soft-delete to Trash.
        // Permanent purge happens via AdminTrash endpoints or the TrashPurge
        // background worker after TTL.
        var assetCollections = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(asset.Id, ct);
        var partialDelete = await TryPartialDeleteAsync(asset.Id, asset.Title, userId, assetCollections, ct);
        if (partialDelete is not null)
            return partialDelete;

        // Soft-delete + audit atomic (A-4).
        await _uow.ExecuteAsync(async tct =>
        {
            await _deletionService.SoftDeleteAsync(asset, userId, tct);
            await _audit.LogAsync("asset.deleted", Constants.ScopeTypes.Asset, asset.Id, userId,
                new() { [AuditKeyTitle] = asset.Title }, tct);
        }, ct);

        await _cache.RemoveByTagAsync(CacheKeys.Tags.Dashboard, ct);
        return ServiceResult.Success;
    }

    /// <summary>
    /// When the user lacks Manager access to all collections, removes the asset only
    /// from the collections they do manage and returns a success result.
    /// Returns null when the user can permanently delete the asset (either admin or full access).
    /// </summary>
    private async Task<ServiceResult?> TryPartialDeleteAsync(
        Guid assetId, string assetTitle, string userId,
        List<Guid> assetCollections, CancellationToken ct)
    {
        if (_currentUser.IsSystemAdmin)
            return null;

        var authorizedCollectionIds = await _authService.FilterAccessibleAsync(
            userId, assetCollections, RoleHierarchy.Roles.Manager, ct);
        if (authorizedCollectionIds.Count == 0)
            return ServiceError.Forbidden();

        if (!assetCollections.Except(authorizedCollectionIds).Any())
            return null; // user manages all collections → allow permanent delete

        // Partial unlink batch + audit atomic (A-4) — leaves ACL state and audit
        // trail consistent if anything in the batch throws midway.
        await _uow.ExecuteAsync(async tct =>
        {
            foreach (var collId in authorizedCollectionIds)
                await _assetCollectionRepo.RemoveFromCollectionAsync(assetId, collId, tct);

            await _audit.LogAsync("asset.removed_from_collections", Constants.ScopeTypes.Asset, assetId, userId,
                new() { [AuditKeyTitle] = assetTitle, ["count"] = authorizedCollectionIds.Count.ToString() },
                tct);
        }, ct);
        return ServiceResult.Success;
    }

    public async Task<ServiceResult<AssetAddedToCollectionResponse>> AddToCollectionAsync(
        Guid assetId, Guid collectionId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var asset = await _assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null)
            return ServiceError.NotFound(AssetNotFoundMessage);

        if (!_currentUser.IsSystemAdmin)
        {
            var assetCollections = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
            var canAccessAsset = assetCollections.Count > 0
                && (await _authService.FilterAccessibleAsync(userId, assetCollections, RoleHierarchy.Roles.Contributor, ct)).Count > 0;

            if (!canAccessAsset && assetCollections.Count == 0)
                canAccessAsset = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);

            if (!canAccessAsset)
                return ServiceError.Forbidden("You don't have permission to manage this asset");

            var canAccessTarget = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Contributor, ct);
            if (!canAccessTarget)
                return ServiceError.Forbidden("You don't have permission to add assets to this collection");
        }

        if (!await _collectionRepo.ExistsAsync(collectionId, ct))
            return ServiceError.NotFound("Collection not found");

        // Add link + audit atomic (A-4).
        var result = await _uow.ExecuteAsync(async tct =>
        {
            var link = await _assetCollectionRepo.AddToCollectionAsync(assetId, collectionId, userId, tct);
            if (link is null) return link;
            await _audit.LogAsync("asset.added_to_collection", Constants.ScopeTypes.Asset, assetId, userId,
                new() { [AuditKeyTitle] = asset.Title, ["collectionId"] = collectionId.ToString() }, tct);
            return link;
        }, ct);

        if (result is null)
            return ServiceError.Conflict("Asset is already linked to this collection");

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
        if (asset is null)
            return ServiceError.NotFound(AssetNotFoundMessage);

        if (!_currentUser.IsSystemAdmin)
        {
            var canAccess = await _authService.CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Manager, ct);
            if (!canAccess)
                return ServiceError.Forbidden("You don't have permission to manage this asset in this collection");
        }

        // Unlink (and possible soft-delete on orphan) + audit atomic (A-4).
        var (removed, _) = await _uow.ExecuteAsync(async tct =>
        {
            var outcome = await _deletionService.RemoveFromCollectionAsync(asset, collectionId, userId, _bucketName, tct);
            if (!outcome.Removed) return outcome;

            if (outcome.SoftDeleted)
            {
                await _audit.LogAsync("asset.deleted", Constants.ScopeTypes.Asset, assetId, userId,
                    new() { [AuditKeyTitle] = asset.Title, ["reason"] = "orphaned" }, tct);
            }
            else
            {
                await _audit.LogAsync("asset.removed_from_collection", Constants.ScopeTypes.Asset, assetId, userId,
                    new() { [AuditKeyTitle] = asset.Title, ["collectionId"] = collectionId.ToString() }, tct);
            }
            return outcome;
        }, ct);

        if (!removed)
            return ServiceError.NotFound("Asset is not linked to this collection");

        return ServiceResult.Success;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    public async Task<ServiceResult<BulkDeleteAssetsResponse>> BulkDeleteAsync(
        BulkDeleteAssetsRequest request, CancellationToken ct)
    {
        if (request.AssetIds.Count == 0)
            return ServiceError.BadRequest("No asset IDs provided");

        if (request.AssetIds.Count > Constants.Limits.MaxPageSize)
            return ServiceError.BadRequest($"Cannot bulk-delete more than {Constants.Limits.MaxPageSize} assets at once");

        var deleted = 0;
        var errors = new List<BulkAssetError>();

        foreach (var assetId in request.AssetIds.Distinct())
        {
            var result = await DeleteAsync(assetId, request.FromCollectionId, ct);
            if (result.IsSuccess)
            {
                deleted++;
            }
            else
            {
                errors.Add(new BulkAssetError
                {
                    AssetId = assetId,
                    Error = result.Error?.Message ?? "Unknown error"
                });
            }
        }

        return new BulkDeleteAssetsResponse
        {
            Message = errors.Count == 0
                ? $"Successfully deleted {deleted} asset(s)"
                : $"Deleted {deleted} asset(s), {errors.Count} failed",
            Deleted = deleted,
            Failed = errors.Count,
            Errors = errors
        };
    }

    private async Task<bool> CanAccessAssetAsync(
        Guid assetId, string requiredRole, CancellationToken ct)
    {
        if (_currentUser.IsSystemAdmin)
            return true;

        var collections = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        var accessible = await _authService.FilterAccessibleAsync(_currentUser.UserId, collections, requiredRole, ct);
        return accessible.Count > 0;
    }

    private async Task<string> GetUserRoleForAssetAsync(Guid assetId, CancellationToken ct)
    {
        if (_currentUser.IsSystemAdmin)
            return RoleHierarchy.Roles.Admin;

        var collections = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        if (collections.Count == 0)
            return RoleHierarchy.Roles.Viewer;

        var roles = await _authService.GetUserRolesAsync(_currentUser.UserId, collections, ct);

        return roles.Values
            .Where(role => role is not null)
            .Aggregate(RoleHierarchy.Roles.Viewer, (best, role) =>
                RoleHierarchy.GetLevel(role!) > RoleHierarchy.GetLevel(best) ? role! : best);
    }
}
