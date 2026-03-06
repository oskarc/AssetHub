using AssetHub.Application;
using AssetHub.Application.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Jobs;

/// <summary>
/// Recurring job that deletes audit events older than a configurable retention period.
/// Deletes in batches to avoid long-running transactions and table locks.
/// </summary>
public class AuditRetentionJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AuditRetentionJob> logger)
{
    private const int BatchSize = 10_000;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var retentionDays = configuration.GetValue("AuditRetention:RetentionDays", Constants.Limits.AuditRetentionDays);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        logger.LogInformation(
            "Starting audit retention cleanup (retaining {Days} days, cutoff: {Cutoff:O})",
            retentionDays, cutoff);

        using var scope = scopeFactory.CreateScope();
        var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();

        var totalDeleted = 0;
        try
        {
            int deleted;
            do
            {
                ct.ThrowIfCancellationRequested();
                deleted = await auditRepo.DeleteOlderThanBatchAsync(cutoff, BatchSize, ct);
                totalDeleted += deleted;

                if (deleted > 0)
                    logger.LogDebug("Audit retention batch: deleted {Deleted} events ({Total} total so far)",
                        deleted, totalDeleted);
            } while (deleted >= BatchSize);

            if (totalDeleted > 0)
                logger.LogInformation("Audit retention cleanup complete: {Deleted} events removed", totalDeleted);
            else
                logger.LogDebug("Audit retention cleanup complete: no events older than {Days} days found",
                    retentionDays);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Audit retention cleanup cancelled after deleting {Deleted} events", totalDeleted);
        }
    }
}
