using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Read-only collection queries.
/// </summary>
public sealed class CollectionQueryService(
    ICollectionRepository collectionRepo,
    ICollectionAuthorizationService authService,
    CurrentUser currentUser) : ICollectionQueryService
{
    public async Task<ServiceResult<List<CollectionResponseDto>>> GetRootCollectionsAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var collections = await collectionRepo.GetAccessibleCollectionsAsync(userId, ct);
        var collectionList = collections.ToList();
        var collectionIds = collectionList.Select(c => c.Id);
        var assetCounts = await collectionRepo.GetAssetCountsAsync(collectionIds, ct);

        var dtos = await CollectionMapper.ToDtoListAsync(collectionList, userId, authService, assetCounts, ct);
        return dtos;
    }

    public async Task<ServiceResult<CollectionResponseDto>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var hasAccess = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Viewer, ct);
        if (!hasAccess)
            return ServiceError.Forbidden();

        var collection = await collectionRepo.GetByIdAsync(id, ct: ct);
        if (collection == null)
            return ServiceError.NotFound("Collection not found");

        var assetCounts = await collectionRepo.GetAssetCountsAsync([id], ct);
        var assetCount = assetCounts.GetValueOrDefault(id);

        var dto = await CollectionMapper.ToDtoAsync(collection, userId, authService, assetCount, ct);
        return dto;
    }

    public async Task<ServiceResult<CollectionDeletionContextDto>> GetDeletionContextAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var hasAccess = await authService.CheckAccessAsync(userId, id, RoleHierarchy.Roles.Manager, ct);
        if (!hasAccess)
            return ServiceError.Forbidden();

        var exists = await collectionRepo.ExistsAsync(id, ct);
        if (!exists)
            return ServiceError.NotFound("Collection not found");

        var assetCounts = await collectionRepo.GetAssetCountsAsync([id], ct);
        var totalAssetCount = assetCounts.GetValueOrDefault(id);
        var orphanedCount = await collectionRepo.GetOrphanedAssetCountAsync(id, ct);

        return new CollectionDeletionContextDto
        {
            TotalAssetCount = totalAssetCount,
            OrphanedAssetCount = orphanedCount
        };
    }
}
