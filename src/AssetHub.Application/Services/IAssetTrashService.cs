using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Admin-only operations on the soft-deleted asset Trash. Restores bring rows back into
/// circulation; purges hard-delete the row + MinIO objects + share links. The
/// TrashPurgeBackgroundService calls PurgeAsync on its own schedule for TTL expiry.
/// </summary>
public interface IAssetTrashService
{
    /// <summary>Paginated list of soft-deleted assets, newest first by DeletedAt.</summary>
    Task<ServiceResult<TrashListResponse>> GetAsync(int skip, int take, CancellationToken ct);

    /// <summary>Restore a single trashed asset — clears DeletedAt and returns it to its collections.</summary>
    Task<ServiceResult> RestoreAsync(Guid id, CancellationToken ct);

    /// <summary>Permanently purge a single trashed asset.</summary>
    Task<ServiceResult> PurgeAsync(Guid id, CancellationToken ct);

    /// <summary>Permanently purge every soft-deleted asset. Returns counts so callers can confirm.</summary>
    Task<ServiceResult<EmptyTrashResponse>> EmptyAsync(CancellationToken ct);
}
