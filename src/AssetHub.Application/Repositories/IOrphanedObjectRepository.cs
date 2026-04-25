using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Persists tombstones for MinIO objects whose owning DB rows have been
/// deleted. The sweeper drains the queue out-of-band so user-facing
/// purge/delete paths can commit atomically without waiting on storage.
/// </summary>
public interface IOrphanedObjectRepository
{
    /// <summary>
    /// Enqueue a single tombstone. Does not call SaveChanges — the caller's
    /// UnitOfWork transaction commits the row.
    /// </summary>
    Task EnqueueAsync(OrphanedObject obj, CancellationToken ct = default);

    /// <summary>
    /// Enqueue many tombstones in one round-trip. Skips entries with a
    /// null/empty ObjectKey.
    /// </summary>
    Task EnqueueBatchAsync(IEnumerable<OrphanedObject> objs, CancellationToken ct = default);

    /// <summary>
    /// Returns the next batch of tombstones for the sweeper to attempt.
    /// Ordered oldest-first; rows with too many failed attempts are skipped
    /// so a poison pill can't block progress on healthy entries.
    /// </summary>
    Task<List<OrphanedObject>> GetNextBatchAsync(int take, int maxAttempts, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task RecordFailureAsync(Guid id, string error, CancellationToken ct = default);
}
