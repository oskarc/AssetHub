using AssetHub.Application;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public class AssetDeletionService(
    IAssetRepository assetRepository,
    IAssetCollectionRepository assetCollectionRepo,
    IShareRepository shareRepository,
    IMinIOAdapter minioAdapter) : IAssetDeletionService
{
    public async Task SoftDeleteAsync(Asset asset, string userId, CancellationToken ct = default)
    {
        if (asset.DeletedAt is not null) return;
        asset.MarkDeleted(userId);
        await assetRepository.UpdateAsync(asset, ct);
    }

    public async Task RestoreAsync(Asset asset, CancellationToken ct = default)
    {
        if (asset.DeletedAt is null) return;
        asset.MarkRestored();
        await assetRepository.UpdateAsync(asset, ct);
    }

    public async Task PurgeAsync(Asset asset, string bucketName, CancellationToken ct = default)
    {
        await shareRepository.DeleteByScopeAsync(Constants.ScopeTypes.Asset, asset.Id, ct);
        await minioAdapter.DeleteAssetObjectsAsync(bucketName, asset, ct);
        await assetRepository.DeleteAsync(asset.Id, ct);
    }

    public async Task<(bool Removed, bool SoftDeleted)> RemoveFromCollectionAsync(
        Asset asset, Guid collectionId, string userId, string bucketName, CancellationToken ct = default)
    {
        // bucketName retained on the signature for symmetry with other methods even though
        // the soft-delete path doesn't touch storage — purge happens later via the worker.
        _ = bucketName;

        var removed = await assetCollectionRepo.RemoveFromCollectionAsync(asset.Id, collectionId, ct);
        if (!removed)
            return (false, false);

        var remaining = await assetCollectionRepo.GetCollectionIdsForAssetAsync(asset.Id, ct);
        if (remaining.Count == 0)
        {
            await SoftDeleteAsync(asset, userId, ct);
            return (true, true);
        }

        return (true, false);
    }

    public async Task<List<Asset>> DeleteCollectionAssetsAsync(
        Guid collectionId, string bucketName, CancellationToken ct = default)
    {
        // Collection deletion still hard-purges exclusive assets — collection-level soft-delete
        // is tracked separately as T1-LIFE-02. Behaviour unchanged from before T1-LIFE-01.
        var deletedAssets = await assetRepository.DeleteByCollectionAsync(collectionId, ct);
        var assetIds = deletedAssets.Select(a => a.Id).ToList();
        await shareRepository.DeleteByScopeBatchAsync(Constants.ScopeTypes.Asset, assetIds, ct);
        await minioAdapter.DeleteAssetObjectsBatchAsync(bucketName, deletedAssets, ct);
        return deletedAssets;
    }
}
