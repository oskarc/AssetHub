using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class AssetDeletionService(
    IAssetRepository assetRepository,
    IAssetCollectionRepository assetCollectionRepo,
    IAssetVersionRepository versionRepository,
    IShareRepository shareRepository,
    IOrphanedObjectRepository orphanedRepo) : IAssetDeletionService
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
        // Collect every MinIO key tied to this asset (live row + version
        // history) BEFORE deleting the asset row, since the FK cascade on
        // AssetVersion will drop the version rows along with the asset.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AddIfNotEmpty(keys, asset.OriginalObjectKey);
        AddIfNotEmpty(keys, asset.ThumbObjectKey);
        AddIfNotEmpty(keys, asset.MediumObjectKey);
        AddIfNotEmpty(keys, asset.PosterObjectKey);

        var versions = await versionRepository.GetByAssetIdAsync(asset.Id, ct);
        foreach (var v in versions)
        {
            AddIfNotEmpty(keys, v.OriginalObjectKey);
            AddIfNotEmpty(keys, v.ThumbObjectKey);
            AddIfNotEmpty(keys, v.MediumObjectKey);
            AddIfNotEmpty(keys, v.PosterObjectKey);
        }

        // DB-only mutations — the caller's UnitOfWork transaction commits
        // share cleanup, asset delete (FK-cascades versions), and tombstone
        // inserts together. The MinIO sweeper drains tombstones out-of-band.
        await shareRepository.DeleteByScopeAsync(Constants.ScopeTypes.Asset, asset.Id, ct);
        await assetRepository.DeleteAsync(asset.Id, ct);
        await EnqueueTombstonesAsync(keys, bucketName, ct);
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

        // MinIO objects are queued for the sweeper rather than deleted inline,
        // so the caller's UoW transaction can wrap the whole batch atomically.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in deletedAssets)
        {
            AddIfNotEmpty(keys, asset.OriginalObjectKey);
            AddIfNotEmpty(keys, asset.ThumbObjectKey);
            AddIfNotEmpty(keys, asset.MediumObjectKey);
            AddIfNotEmpty(keys, asset.PosterObjectKey);
        }
        await EnqueueTombstonesAsync(keys, bucketName, ct);
        return deletedAssets;
    }

    private static void AddIfNotEmpty(HashSet<string> keys, string? key)
    {
        if (!string.IsNullOrEmpty(key)) keys.Add(key);
    }

    private async Task EnqueueTombstonesAsync(
        IReadOnlyCollection<string> keys, string bucketName, CancellationToken ct)
    {
        if (keys.Count == 0) return;
        var now = DateTime.UtcNow;
        var rows = keys.Select(k => new OrphanedObject
        {
            Id = Guid.NewGuid(),
            BucketName = bucketName,
            ObjectKey = k,
            CreatedAt = now
        });
        await orphanedRepo.EnqueueBatchAsync(rows, ct);
    }
}
