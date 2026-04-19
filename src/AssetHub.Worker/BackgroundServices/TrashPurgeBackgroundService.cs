using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Drains the Trash of assets older than AssetLifecycleSettings.TrashRetentionDays.
/// Runs every PurgeIntervalMinutes (default 60). Each iteration pulls a bounded batch,
/// purges with per-item try/catch so one failed row can't poison the run, and logs counts.
/// </summary>
public sealed class TrashPurgeBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<AssetLifecycleSettings> lifecycleSettings,
    ILogger<TrashPurgeBackgroundService> logger) : BackgroundService
{
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = lifecycleSettings.Value;
        var interval = TimeSpan.FromMinutes(settings.PurgeIntervalMinutes);

        logger.LogInformation(
            "Trash purge worker started. Retention: {Days} days, interval: {Minutes} min",
            settings.TrashRetentionDays, settings.PurgeIntervalMinutes);

        // Run immediately on startup so boot-time cleanup happens without waiting the full interval.
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunPurgeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Trash purge run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var assetRepo = scope.ServiceProvider.GetRequiredService<IAssetRepository>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAssetDeletionService>();
        var minioSettings = scope.ServiceProvider.GetRequiredService<IOptions<MinIOSettings>>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IOptions<AssetLifecycleSettings>>();

        var bucketName = minioSettings.Value.BucketName;
        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(lifecycle.Value.TrashRetentionDays);

        int purged = 0;
        int failed = 0;

        while (!ct.IsCancellationRequested)
        {
            var expired = await assetRepo.GetTrashOlderThanAsync(cutoff, BatchSize, ct);
            if (expired.Count == 0) break;

            foreach (var asset in expired)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await deletionService.PurgeAsync(asset, bucketName, ct);
                    purged++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    logger.LogWarning(ex, "Failed to purge expired trash asset {AssetId}", asset.Id);
                }
            }

            // If the whole batch failed we'd loop forever on the same rows; bail out.
            if (failed == expired.Count && purged == 0) break;
            if (expired.Count < BatchSize) break;
        }

        if (purged > 0 || failed > 0)
        {
            logger.LogInformation("Trash purge completed: {Purged} purged, {Failed} failed", purged, failed);
        }
        else
        {
            logger.LogDebug("Trash purge: no expired rows");
        }
    }
}
