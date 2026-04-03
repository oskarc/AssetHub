using AssetHub.Application;
using AssetHub.Application.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Deletes audit events older than a configurable retention period.
/// Runs weekly on Sundays at approximately 5:00 AM UTC.
/// </summary>
public sealed class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    private const int BatchSize = 10_000;
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until roughly 5:00 AM UTC on first run
        var now = DateTime.UtcNow;
        var nextRun = now.Date.AddHours(5);
        if (nextRun <= now) nextRun = nextRun.AddDays(1);
        var initialDelay = nextRun - now;

        logger.LogInformation("Audit retention cleanup scheduled, first run in {Delay}", initialDelay);
        await Task.Delay(initialDelay, stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunRetentionAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Audit retention cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunRetentionAsync(CancellationToken ct)
    {
        var retentionDays = configuration.GetValue("AuditRetention:RetentionDays", Constants.Limits.AuditRetentionDays);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        logger.LogInformation(
            "Starting audit retention cleanup (retaining {Days} days, cutoff: {Cutoff:O})",
            retentionDays, cutoff);

        using var scope = scopeFactory.CreateScope();
        var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();

        var totalDeleted = 0;
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
}
