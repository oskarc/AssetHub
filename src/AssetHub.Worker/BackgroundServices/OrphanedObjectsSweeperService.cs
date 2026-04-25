using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Drains the OrphanedObjects queue: each row points at a MinIO object whose
/// owning DB row has already been deleted. The sweeper issues the DELETE on
/// MinIO and then removes the tombstone. Failures bump AttemptCount + LastError
/// so a poison object eventually drops out of the work-set instead of blocking
/// the queue forever.
/// </summary>
public sealed class OrphanedObjectsSweeperService(
    IServiceScopeFactory scopeFactory,
    ILogger<OrphanedObjectsSweeperService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 100;
    private const int MaxAttempts = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Orphaned-objects sweeper started. Interval: {Seconds}s, batch: {Batch}, max attempts: {MaxAttempts}",
            Interval.TotalSeconds, BatchSize, MaxAttempts);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Orphaned-objects sweep failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var orphanedRepo = scope.ServiceProvider.GetRequiredService<IOrphanedObjectRepository>();
        var minio = scope.ServiceProvider.GetRequiredService<IMinIOAdapter>();

        var batch = await orphanedRepo.GetNextBatchAsync(BatchSize, MaxAttempts, ct);
        if (batch.Count == 0)
        {
            logger.LogDebug("Orphaned-objects sweep: queue empty");
            return;
        }

        var deleted = 0;
        var failed = 0;
        foreach (var row in batch)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await minio.DeleteAsync(row.BucketName, row.ObjectKey, ct);
                await orphanedRepo.DeleteAsync(row.Id, ct);
                deleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await orphanedRepo.RecordFailureAsync(row.Id, ex.Message, ct);
                failed++;
                logger.LogWarning(ex,
                    "Failed to delete orphaned object {Bucket}/{Key} (attempt {Attempt})",
                    row.BucketName, row.ObjectKey, row.AttemptCount + 1);
            }
        }

        logger.LogInformation(
            "Orphaned-objects sweep: {Deleted} deleted, {Failed} failed (batch {BatchCount})",
            deleted, failed, batch.Count);
    }
}
