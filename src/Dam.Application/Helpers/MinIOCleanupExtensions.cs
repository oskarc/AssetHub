using Dam.Application.Services;
using Dam.Domain.Entities;

namespace Dam.Application.Helpers;

/// <summary>
/// Extension methods for cleaning up MinIO storage objects associated with assets.
/// Centralises the rendition-aware deletion logic to avoid duplication across endpoint files.
/// </summary>
public static class MinIOCleanupExtensions
{
    /// <summary>
    /// Delete all MinIO objects (original + renditions) for a single asset.
    /// </summary>
    public static async Task DeleteAssetObjectsAsync(
        this IMinIOAdapter minio, string bucketName, Asset asset, CancellationToken ct = default)
    {
        await minio.DeleteAsync(bucketName, asset.OriginalObjectKey, ct);
        if (asset.ThumbObjectKey != null)
            await minio.DeleteAsync(bucketName, asset.ThumbObjectKey, ct);
        if (asset.MediumObjectKey != null)
            await minio.DeleteAsync(bucketName, asset.MediumObjectKey, ct);
        if (asset.PosterObjectKey != null)
            await minio.DeleteAsync(bucketName, asset.PosterObjectKey, ct);
    }

    /// <summary>
    /// Delete all MinIO objects for a batch of assets (e.g. after collection deletion).
    /// </summary>
    public static async Task DeleteAssetObjectsBatchAsync(
        this IMinIOAdapter minio, string bucketName, IEnumerable<Asset> assets, CancellationToken ct = default)
    {
        foreach (var asset in assets)
        {
            await minio.DeleteAssetObjectsAsync(bucketName, asset, ct);
        }
    }
}
