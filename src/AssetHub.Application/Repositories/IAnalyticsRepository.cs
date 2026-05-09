using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Read access to the analytics rollup tables (T5-ANL-01). Write access
/// goes through <c>IAnalyticsRollupService.RollupDayAsync</c> so the
/// upsert + multi-table semantics stay encapsulated.
/// </summary>
public interface IAnalyticsRollupRepository
{
    /// <summary>
    /// Sum the <c>Count</c> column over <paramref name="metric"/> for the date range
    /// <c>[fromInclusive, toInclusive]</c>, group by <c>EntityId</c>, return the top
    /// <paramref name="take"/> rows ordered by total desc.
    /// </summary>
    Task<IReadOnlyList<(string EntityId, long Count)>> GetTopByCountAsync(
        AnalyticsCountMetric metric,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int take,
        CancellationToken ct);

    /// <summary>Daily total <c>Count</c> for <paramref name="metric"/> across all entities. Drives trend charts.</summary>
    Task<IReadOnlyList<(DateOnly Date, long Count)>> GetDailyTotalsAsync(
        AnalyticsCountMetric metric,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct);

    /// <summary>
    /// Storage rollup as of the most recent date with rows. Returns the
    /// top <paramref name="take"/> rows ordered by <c>Bytes</c> desc.
    /// </summary>
    Task<IReadOnlyList<(string EntityId, long Bytes, int AssetCount)>> GetLatestStorageAsync(
        AnalyticsStorageMetric metric,
        int take,
        CancellationToken ct);

    /// <summary>
    /// Distinct asset count touched per recipient hash for the date range.
    /// Used by the exposure panel to show "this recipient touched N distinct
    /// assets" alongside the raw download count.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetDistinctAssetCountsByRecipientAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyCollection<string> recipientHashes,
        CancellationToken ct);
}

/// <summary>
/// CRUD for the queued PDF-export jobs table (T5-ANL-01).
/// </summary>
public interface IAnalyticsPdfJobRepository
{
    Task<AnalyticsPdfJob?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(AnalyticsPdfJob job, CancellationToken ct);
    Task UpdateAsync(AnalyticsPdfJob job, CancellationToken ct);
    /// <summary>Delete jobs that have expired (their MinIO object should already be gone).</summary>
    Task<int> DeleteExpiredAsync(DateTime now, CancellationToken ct);
}
