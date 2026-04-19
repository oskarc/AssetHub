using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Read + restore + prune operations on the per-asset version history. Snapshot capture
/// happens inside AssetUploadService.ReplaceImageFileAsync — there's no public "create version"
/// surface because version creation is always tied to a specific mutation.
/// </summary>
public interface IAssetVersionService
{
    /// <summary>
    /// Returns the version history for an asset, newest first. Caller must have at least
    /// Viewer access to the asset.
    /// </summary>
    Task<ServiceResult<List<AssetVersionDto>>> GetForAssetAsync(Guid assetId, CancellationToken ct);

    /// <summary>
    /// Restore a prior version as the current asset state. Captures the current state into a
    /// new version row first (so restore is itself reversible), then overwrites the asset row
    /// with the chosen version's snapshot. Caller must have Contributor access.
    /// </summary>
    Task<ServiceResult<AssetVersionDto>> RestoreAsync(Guid assetId, int versionNumber, CancellationToken ct);

    /// <summary>
    /// Permanently remove a single version from history (admin only). The version's MinIO
    /// objects are deleted unless still referenced by the asset's current state.
    /// </summary>
    Task<ServiceResult> PruneAsync(Guid assetId, int versionNumber, CancellationToken ct);
}
