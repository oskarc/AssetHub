using AssetHub.Domain.Entities;

namespace AssetHub.Application.Services;

/// <summary>
/// Asset deletion orchestration: soft-delete to Trash, hard-purge of MinIO + DB,
/// and orphan handling on collection removal. Authorization must be verified by
/// the caller before invoking these methods.
/// </summary>
public interface IAssetDeletionService
{
    /// <summary>
    /// Moves an asset to Trash by stamping DeletedAt + DeletedByUserId. The row stays;
    /// MinIO objects stay; AssetCollection links stay (so restore returns the asset to
    /// its original collections). A background worker purges the row after the
    /// AssetLifecycleSettings.TrashRetentionDays window.
    /// </summary>
    Task SoftDeleteAsync(Asset asset, string userId, CancellationToken ct = default);

    /// <summary>
    /// Restores a soft-deleted asset by clearing DeletedAt. Idempotent on never-deleted assets.
    /// </summary>
    Task RestoreAsync(Asset asset, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes an asset: deletes share links, deletes all MinIO renditions, and
    /// deletes the DB row. No recovery. Used by the trash-purge worker, the admin
    /// "delete forever" endpoint, the stale-upload cleanup worker, and malware-rejection paths.
    /// </summary>
    Task PurgeAsync(Asset asset, string bucketName, CancellationToken ct = default);

    /// <summary>
    /// Removes an asset from a collection. If the asset becomes orphaned (no remaining
    /// collections), it is soft-deleted instead of being purged — the user can restore from
    /// Trash within the retention window.
    /// </summary>
    /// <returns>(Removed: whether the link existed, SoftDeleted: whether the asset was orphan-soft-deleted)</returns>
    Task<(bool Removed, bool SoftDeleted)> RemoveFromCollectionAsync(
        Asset asset, Guid collectionId, string userId, string bucketName, CancellationToken ct = default);

    /// <summary>
    /// Handles collection deletion: permanently purges exclusive assets (and their storage),
    /// unlinks shared assets. Returns the list of permanently purged assets for audit purposes.
    /// Collection-level soft-delete is tracked separately as T1-LIFE-02.
    /// </summary>
    Task<List<Asset>> DeleteCollectionAssetsAsync(
        Guid collectionId, string bucketName, CancellationToken ct = default);
}
