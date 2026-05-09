using AssetHub.Application;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Write-side analytics rollup (T5-ANL-01). Reads <c>asset.downloaded</c>
/// + <c>collection.downloaded</c> audit events and <c>WatermarkDownload</c>
/// rows for a single UTC day, plus a snapshot of current asset sizes, then
/// upserts into the rollup tables.
///
/// Idempotent on (Date, Metric, EntityId): re-running the rollup for the
/// same day overwrites the count. This is intentional — it lets a missed
/// day be back-filled later without duplicate-counting, and it lets the
/// service safely run hourly while the audit-retention sweeper deletes
/// raw rows out from under it (the rollup, once written, becomes the
/// long-term source of truth).
/// </summary>
public sealed class AnalyticsRollupService(
    DbContextProvider dbContextProvider,
    IAuditService auditService,
    ILogger<AnalyticsRollupService> logger) : IAnalyticsRollupService
{
    public async Task<ServiceResult<RollupResult>> RollupDayAsync(DateOnly day, CancellationToken ct)
    {
        var fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var now = DateTime.UtcNow;

        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var db = lease.Db;

        var downloadRows = await BuildDownloadRowsAsync(db, day, fromUtc, toUtc, ct);
        var exposureRows = await BuildExposureRowsAsync(db, day, fromUtc, toUtc, now, ct);
        var storageRows = await BuildStorageRowsAsync(db, day, now, ct);

        // Upsert: stamp UpdatedAt regardless. EF Core doesn't have a
        // first-class upsert; we use ExecuteUpdate per row only when needed,
        // but the simpler path is delete-then-insert for the day's slice.
        // The slice is small (rows-per-day, capped by entity count) so this
        // is cheap and avoids the per-row trip.
        await db.AnalyticsDailyRollups
            .Where(r => r.Date == day)
            .ExecuteDeleteAsync(ct);
        await db.AnalyticsStorageRollups
            .Where(r => r.Date == day)
            .ExecuteDeleteAsync(ct);

        if (downloadRows.Count > 0) db.AnalyticsDailyRollups.AddRange(downloadRows);
        if (exposureRows.Count > 0) db.AnalyticsDailyRollups.AddRange(exposureRows);
        if (storageRows.Count > 0) db.AnalyticsStorageRollups.AddRange(storageRows);
        await db.SaveChangesAsync(ct);

        var result = new RollupResult(
            Day: day,
            DownloadRowsWritten: downloadRows.Count,
            ExposureRowsWritten: exposureRows.Count,
            StorageRowsWritten: storageRows.Count);

        // Audit one row per successful rollup so a retention auditor can
        // see "the rollup outlived the source events for this day."
        try
        {
            await auditService.LogAsync(
                eventType: AnalyticsConstants.AuditEvents.RollupCompleted,
                targetType: AnalyticsConstants.ScopeTypes.Analytics,
                targetId: Guid.Empty,
                actorUserId: null,
                details: new Dictionary<string, object>
                {
                    ["date"] = day.ToString("yyyy-MM-dd"),
                    ["download_rows"] = downloadRows.Count,
                    ["exposure_rows"] = exposureRows.Count,
                    ["storage_rows"] = storageRows.Count
                },
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Analytics rollup audit failed for {Day}", day);
        }

        logger.LogInformation(
            "Analytics rollup for {Day} wrote {DownloadRows} download / {ExposureRows} exposure / {StorageRows} storage rows",
            day, downloadRows.Count, exposureRows.Count, storageRows.Count);
        return result;
    }

    private static async Task<List<AnalyticsDailyRollup>> BuildDownloadRowsAsync(
        AssetHubDbContext db, DateOnly day, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var rows = new List<AnalyticsDailyRollup>();
        var now = DateTime.UtcNow;

        // Asset-level downloads — group asset.downloaded events by asset id.
        // TargetId is nullable on AuditEvent (system events have no target);
        // filter those out before grouping.
        var assetDownloads = await db.AuditEvents
            .AsNoTracking()
            .Where(e => e.EventType == "asset.downloaded"
                     && e.TargetType == Constants.ScopeTypes.Asset
                     && e.TargetId != null
                     && e.CreatedAt >= fromUtc && e.CreatedAt < toUtc)
            .GroupBy(e => e.TargetId!.Value)
            .Select(g => new { AssetId = g.Key, Count = (long)g.Count() })
            .ToListAsync(ct);

        foreach (var row in assetDownloads)
        {
            rows.Add(new AnalyticsDailyRollup
            {
                Date = day,
                Metric = AnalyticsCountMetric.DownloadsByAsset,
                EntityId = row.AssetId.ToString("D"),
                Count = row.Count,
                UpdatedAt = now
            });
        }

        // Collection-level downloads — collection.downloaded fires when a ZIP
        // build completes; collection.download_requested fires when one is
        // queued. Count the completion events; queued-but-not-completed are
        // tracked separately by ZipDownload status.
        var collectionDownloads = await db.AuditEvents
            .AsNoTracking()
            .Where(e => e.EventType == "collection.downloaded"
                     && e.TargetType == Constants.ScopeTypes.Collection
                     && e.TargetId != null
                     && e.CreatedAt >= fromUtc && e.CreatedAt < toUtc)
            .GroupBy(e => e.TargetId!.Value)
            .Select(g => new { CollectionId = g.Key, Count = (long)g.Count() })
            .ToListAsync(ct);

        foreach (var row in collectionDownloads)
        {
            rows.Add(new AnalyticsDailyRollup
            {
                Date = day,
                Metric = AnalyticsCountMetric.DownloadsByCollection,
                EntityId = row.CollectionId.ToString("D"),
                Count = row.Count,
                UpdatedAt = now
            });
        }

        return rows;
    }

    private static async Task<List<AnalyticsDailyRollup>> BuildExposureRowsAsync(
        AssetHubDbContext db, DateOnly day, DateTime fromUtc, DateTime toUtc, DateTime now, CancellationToken ct)
    {
        // Exposure rows hash-group WatermarkDownloads. A single recipient may
        // have both UserId and Email hashes set; treat them as distinct rows
        // because the reveal endpoint resolves them separately. Skip rows
        // where both hashes are null (anonymous-share watermark with no
        // identifying recipient — counts toward the per-asset download but
        // not toward exposure-by-recipient).
        var rows = await db.WatermarkDownloads
            .AsNoTracking()
            .Where(w => w.DownloadedAt >= fromUtc && w.DownloadedAt < toUtc
                     && (w.RecipientUserIdHash != null || w.RecipientEmailHash != null))
            .Select(w => new
            {
                w.RecipientUserIdHash,
                w.RecipientEmailHash
            })
            .ToListAsync(ct);

        var grouped = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var w in rows)
        {
            if (w.RecipientUserIdHash != null)
            {
                var key = Convert.ToBase64String(w.RecipientUserIdHash);
                grouped[key] = grouped.GetValueOrDefault(key) + 1;
            }
            else if (w.RecipientEmailHash != null)
            {
                var key = Convert.ToBase64String(w.RecipientEmailHash);
                grouped[key] = grouped.GetValueOrDefault(key) + 1;
            }
        }

        return grouped
            .Select(kvp => new AnalyticsDailyRollup
            {
                Date = day,
                Metric = AnalyticsCountMetric.ExposureByRecipient,
                EntityId = kvp.Key,
                Count = kvp.Value,
                UpdatedAt = now
            })
            .ToList();
    }

    private static async Task<List<AnalyticsStorageRollup>> BuildStorageRowsAsync(
        AssetHubDbContext db, DateOnly day, DateTime now, CancellationToken ct)
    {
        // Storage is a snapshot of "current" — it doesn't make sense to roll
        // up a historical day's storage from already-deleted assets. We
        // capture today's bytes-by-collection / bytes-by-type and store them
        // under today's date; the trend chart joins multiple days to show
        // how storage grew.
        var rows = new List<AnalyticsStorageRollup>();

        // Storage-by-collection — sum SizeBytes across the AssetCollection
        // join, including the same asset under multiple collections (the
        // forensic question is "what would I lose if collection X went
        // away", not "what is unique to X"). Skip soft-deleted assets.
        var collectionStorage = await db.AssetCollections
            .AsNoTracking()
            .Where(ac => ac.Asset.DeletedAt == null)
            .GroupBy(ac => ac.CollectionId)
            .Select(g => new
            {
                CollectionId = g.Key,
                Bytes = g.Sum(ac => ac.Asset.SizeBytes),
                AssetCount = g.Count()
            })
            .ToListAsync(ct);

        foreach (var row in collectionStorage)
        {
            rows.Add(new AnalyticsStorageRollup
            {
                Date = day,
                Metric = AnalyticsStorageMetric.StorageByCollection,
                EntityId = row.CollectionId.ToString("D"),
                Bytes = row.Bytes,
                AssetCount = row.AssetCount,
                UpdatedAt = now
            });
        }

        // Storage-by-asset-type — sum across all live assets. Each asset
        // counted once regardless of how many collections it's in.
        var typeStorage = await db.Assets
            .AsNoTracking()
            .Where(a => a.DeletedAt == null)
            .GroupBy(a => a.AssetType)
            .Select(g => new
            {
                AssetType = g.Key,
                Bytes = g.Sum(a => a.SizeBytes),
                AssetCount = g.Count()
            })
            .ToListAsync(ct);

        foreach (var row in typeStorage)
        {
            rows.Add(new AnalyticsStorageRollup
            {
                Date = day,
                Metric = AnalyticsStorageMetric.StorageByAssetType,
                EntityId = row.AssetType.ToDbString(),
                Bytes = row.Bytes,
                AssetCount = row.AssetCount,
                UpdatedAt = now
            });
        }

        return rows;
    }
}
