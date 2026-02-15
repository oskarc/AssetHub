using Dam.Domain.Entities;

namespace Dam.Application.Services;

/// <summary>
/// Consolidates asset deletion orchestration: MinIO cleanup + DB deletion + orphan handling.
/// Authorization must be verified by the caller before invoking these methods.
/// </summary>
public interface IAssetDeletionService
{
    /// <summary>
    /// Permanently deletes an asset: removes all MinIO renditions and the DB record.
    /// </summary>
    Task PermanentDeleteAsync(Asset asset, string bucketName, CancellationToken ct = default);

    /// <summary>
    /// Removes an asset from a collection. If the asset becomes orphaned (no remaining collections),
    /// it is permanently deleted.
    /// </summary>
    /// <returns>(Removed: whether the link existed, PermanentlyDeleted: whether the asset was orphan-deleted)</returns>
    Task<(bool Removed, bool PermanentlyDeleted)> RemoveFromCollectionAsync(
        Asset asset, Guid collectionId, string bucketName, CancellationToken ct = default);

    /// <summary>
    /// Handles collection deletion: permanently deletes exclusive assets (and their storage),
    /// unlinks shared assets. Returns the list of permanently deleted assets for audit purposes.
    /// </summary>
    Task<List<Asset>> DeleteCollectionAssetsAsync(
        Guid collectionId, string bucketName, CancellationToken ct = default);
}
