using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;

namespace Dam.Infrastructure.Services;

/// <inheritdoc />
public class AssetDeletionService(
    IAssetRepository assetRepository,
    IAssetCollectionRepository assetCollectionRepo,
    IShareRepository shareRepository,
    IMinIOAdapter minioAdapter) : IAssetDeletionService
{
    public async Task PermanentDeleteAsync(Asset asset, string bucketName, CancellationToken ct = default)
    {
        await shareRepository.DeleteByScopeAsync("asset", asset.Id, ct);
        await minioAdapter.DeleteAssetObjectsAsync(bucketName, asset, ct);
        await assetRepository.DeleteAsync(asset.Id, ct);
    }

    public async Task<(bool Removed, bool PermanentlyDeleted)> RemoveFromCollectionAsync(
        Asset asset, Guid collectionId, string bucketName, CancellationToken ct = default)
    {
        var removed = await assetCollectionRepo.RemoveFromCollectionAsync(asset.Id, collectionId, ct);
        if (!removed)
            return (false, false);

        var remaining = await assetCollectionRepo.GetCollectionIdsForAssetAsync(asset.Id, ct);
        if (remaining.Count == 0)
        {
            await PermanentDeleteAsync(asset, bucketName, ct);
            return (true, true);
        }

        return (true, false);
    }

    public async Task<List<Asset>> DeleteCollectionAssetsAsync(
        Guid collectionId, string bucketName, CancellationToken ct = default)
    {
        var deletedAssets = await assetRepository.DeleteByCollectionAsync(collectionId, ct);
        foreach (var asset in deletedAssets)
            await shareRepository.DeleteByScopeAsync("asset", asset.Id, ct);
        await minioAdapter.DeleteAssetObjectsBatchAsync(bucketName, deletedAssets, ct);
        return deletedAssets;
    }
}
