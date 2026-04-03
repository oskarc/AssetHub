using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Cleans up assets stuck in "uploading" status for too long.
/// Runs daily at approximately 3:00 AM UTC.
/// </summary>
public sealed class StaleUploadCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<StaleUploadCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(24);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until roughly 3:00 AM UTC on first run
        var now = DateTime.UtcNow;
        var nextRun = now.Date.AddHours(3);
        if (nextRun <= now) nextRun = nextRun.AddDays(1);
        var initialDelay = nextRun - now;

        logger.LogInformation("Stale upload cleanup scheduled, first run in {Delay}", initialDelay);
        await Task.Delay(initialDelay, stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Stale upload cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var assetRepo = scope.ServiceProvider.GetRequiredService<IAssetRepository>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAssetDeletionService>();
        var minioSettings = scope.ServiceProvider.GetRequiredService<IOptions<MinIOSettings>>();

        var bucketName = minioSettings.Value.BucketName;
        var cutoff = DateTime.UtcNow - StaleThreshold;

        logger.LogInformation("Starting stale upload cleanup (threshold: {Threshold})", StaleThreshold);

        var cleaned = 0;
        const int batchSize = 500;
        int batchCleaned;
        int skip = 0;

        do
        {
            var staleAssets = await assetRepo.GetByStatusAsync(
                AssetStatus.Uploading.ToDbString(), skip: skip, take: batchSize, ct);

            if (staleAssets.Count == 0)
                break;

            batchCleaned = 0;
            foreach (var asset in staleAssets.Where(a => a.CreatedAt < cutoff))
            {
                try
                {
                    await deletionService.PermanentDeleteAsync(asset, bucketName, ct);
                    cleaned++;
                    batchCleaned++;
                    logger.LogInformation("Cleaned up stale upload: {AssetId} ({Title}, created {CreatedAt})",
                        asset.Id, asset.Title, asset.CreatedAt);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up stale upload {AssetId}", asset.Id);
                }
            }

            // If we deleted some, re-fetch the same page (rows shifted).
            // If none were stale in this batch, advance past these records.
            if (batchCleaned == 0)
                skip += staleAssets.Count;

        } while (true);

        logger.LogInformation("Stale upload cleanup complete: {Cleaned} assets removed", cleaned);
    }
}
