using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

/// <summary>
/// Read-side queries against the analytics rollup tables (T5-ANL-01).
/// All writes go through <c>IAnalyticsRollupService.RollupDayAsync</c> so
/// the upsert + multi-table semantics stay encapsulated.
/// </summary>
public sealed class AnalyticsRollupRepository(DbContextProvider provider) : IAnalyticsRollupRepository
{
    public async Task<IReadOnlyList<(string EntityId, long Count)>> GetTopByCountAsync(
        AnalyticsCountMetric metric,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int take,
        CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var rows = await lease.Db.AnalyticsDailyRollups
            .AsNoTracking()
            .Where(r => r.Metric == metric && r.Date >= fromInclusive && r.Date <= toInclusive)
            .GroupBy(r => r.EntityId)
            .Select(g => new { EntityId = g.Key, Count = g.Sum(r => r.Count) })
            .OrderByDescending(g => g.Count)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => (r.EntityId, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<(DateOnly Date, long Count)>> GetDailyTotalsAsync(
        AnalyticsCountMetric metric,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var rows = await lease.Db.AnalyticsDailyRollups
            .AsNoTracking()
            .Where(r => r.Metric == metric && r.Date >= fromInclusive && r.Date <= toInclusive)
            .GroupBy(r => r.Date)
            .Select(g => new { Date = g.Key, Count = g.Sum(r => r.Count) })
            .OrderBy(g => g.Date)
            .ToListAsync(ct);
        return rows.Select(r => (r.Date, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<(string EntityId, long Bytes, int AssetCount)>> GetLatestStorageAsync(
        AnalyticsStorageMetric metric,
        int take,
        CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;

        // Pick the most recent date that has any row for this metric, then pull
        // every entity bucket from that date. Two-step is cheaper than a window
        // function in EF and the date-metric index covers both reads.
        var latestDate = await db.AnalyticsStorageRollups
            .AsNoTracking()
            .Where(r => r.Metric == metric)
            .OrderByDescending(r => r.Date)
            .Select(r => (DateOnly?)r.Date)
            .FirstOrDefaultAsync(ct);

        if (latestDate is null) return [];

        var rows = await db.AnalyticsStorageRollups
            .AsNoTracking()
            .Where(r => r.Metric == metric && r.Date == latestDate.Value)
            .OrderByDescending(r => r.Bytes)
            .Take(take)
            .Select(r => new { r.EntityId, r.Bytes, r.AssetCount })
            .ToListAsync(ct);

        return rows.Select(r => (r.EntityId, r.Bytes, r.AssetCount)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> GetDistinctAssetCountsByRecipientAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyCollection<string> recipientHashes,
        CancellationToken ct)
    {
        if (recipientHashes.Count == 0) return new Dictionary<string, int>();

        // The exposure rollup row's EntityId is the recipient hash. To compute
        // "distinct assets per recipient" we have to join back to WatermarkDownload
        // because the rollup folds asset identity into the count. This is a
        // bounded query — we filter by the top-N recipient hashes from the rollup.
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var hashSet = recipientHashes.ToHashSet();

        var fromUtc = fromInclusive.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // We don't know up-front whether the hash is on UserId or Email, so a
        // single pass picks whichever column matches. Convert byte[] hashes to
        // base64 in the query for set membership against the input strings.
        var rows = await db.WatermarkDownloads
            .AsNoTracking()
            .Where(w => w.DownloadedAt >= fromUtc && w.DownloadedAt < toUtc)
            .Where(w =>
                (w.RecipientUserIdHash != null && hashSet.Contains(Convert.ToBase64String(w.RecipientUserIdHash))) ||
                (w.RecipientEmailHash != null && hashSet.Contains(Convert.ToBase64String(w.RecipientEmailHash))))
            .Select(w => new
            {
                Hash = w.RecipientUserIdHash != null
                    ? Convert.ToBase64String(w.RecipientUserIdHash)
                    : Convert.ToBase64String(w.RecipientEmailHash!),
                w.AssetId
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.Hash)
            .ToDictionary(g => g.Key, g => g.Select(r => r.AssetId).Distinct().Count());
    }
}
