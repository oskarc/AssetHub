using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class AuditRetentionSweeper(
    IAuditEventRepository auditRepo,
    IAuditService auditService,
    IOptions<AuditRetentionSettings> settings,
    ILogger<AuditRetentionSweeper> logger) : IAuditRetentionSweeper
{
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var s = settings.Value;
        var now = DateTime.UtcNow;
        var totalPurged = 0;
        var perTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Per-event-type passes — each drains until a batch returns fewer rows
        // than the cap so a backlogged event type still clears in a single sweep.
        foreach (var (eventType, retentionDays) in s.PerEventTypeOverrides)
        {
            var typeCutoff = now.AddDays(-retentionDays);
            var typePurged = await DrainAsync(
                ct,
                () => auditRepo.DeleteByEventTypeOlderThanBatchAsync(eventType, typeCutoff, s.BatchSize, ct),
                s.BatchSize);
            if (typePurged > 0)
            {
                perTypeCounts[eventType] = typePurged;
                totalPurged += typePurged;
                logger.LogInformation(
                    "Audit retention pass: {EventType} purged {Count} (cutoff {Cutoff:O}, retention {Days} d)",
                    eventType, typePurged, typeCutoff, retentionDays);
            }
        }

        // Default-retention pass for everything not covered by an override.
        var defaultCutoff = now.AddDays(-s.DefaultRetentionDays);
        var excluded = s.PerEventTypeOverrides.Keys.ToArray();
        var defaultPurged = await DrainAsync(
            ct,
            () => auditRepo.DeleteOlderThanBatchExcludingTypesAsync(defaultCutoff, excluded, s.BatchSize, ct),
            s.BatchSize);
        if (defaultPurged > 0)
        {
            totalPurged += defaultPurged;
            logger.LogInformation(
                "Audit retention pass: default purged {Count} (cutoff {Cutoff:O}, retention {Days} d, excluding {Overrides} types)",
                defaultPurged, defaultCutoff, s.DefaultRetentionDays, excluded.Length);
        }

        if (totalPurged == 0)
        {
            logger.LogDebug("Audit retention sweep: nothing to purge");
            return 0;
        }

        // Meta-audit: one row per run summarising what was deleted. Wrapped
        // in try/catch so an audit-write failure (e.g. transient DB blip)
        // doesn't make us roll back the retention work that already
        // committed — that would leave the rows gone with no record.
        try
        {
            var details = new Dictionary<string, object>
            {
                ["purged_count"] = totalPurged,
                ["default_cutoff_date"] = defaultCutoff,
                ["default_retention_days"] = s.DefaultRetentionDays,
                ["per_event_type"] = perTypeCounts,
            };

            await auditService.LogAsync(
                Constants.AuditEvents.AuditRetentionPurged,
                Constants.ScopeTypes.Audit,
                targetId: null,
                actorUserId: null,
                details,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Audit retention sweep purged {Count} rows but failed to write the meta-audit event",
                totalPurged);
        }

        logger.LogInformation(
            "Audit retention sweep complete: {Total} rows purged across {TypeCount} event-type pass(es)",
            totalPurged, perTypeCounts.Count + (defaultPurged > 0 ? 1 : 0));
        return totalPurged;
    }

    /// <summary>
    /// Calls the supplied delete-batch function in a loop until a batch returns
    /// fewer rows than <paramref name="batchSize"/>. Returns the cumulative count.
    /// </summary>
    private static async Task<int> DrainAsync(
        CancellationToken ct, Func<Task<int>> deleteBatch, int batchSize)
    {
        var total = 0;
        int batch;
        do
        {
            ct.ThrowIfCancellationRequested();
            batch = await deleteBatch();
            total += batch;
        } while (batch >= batchSize);
        return total;
    }
}
