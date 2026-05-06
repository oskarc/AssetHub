using AssetHub.Application.Configuration;
using AssetHub.Application.Messages;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Periodically scans for watermark-flagged assets that don't yet have the asset-
/// fingerprint applied to their stored renditions and enqueues
/// <see cref="EmbedAssetFingerprintCommand"/> for each (T5-WMK-01).
///
/// Pure performance optimisation per the no-gap rule — even unfingerprinted assets
/// receive a fully two-layer watermark on the on-the-fly download path. This sweep
/// just pre-bakes the asset layer into MinIO so downloads only have to apply the
/// recipient layer.
/// </summary>
public sealed class WatermarkAssetFingerprintBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<WatermarkSettings> watermarkSettings,
    ILogger<WatermarkAssetFingerprintBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = watermarkSettings.Value;
        var interval = TimeSpan.FromSeconds(settings.BackgroundSweepIntervalSeconds);

        logger.LogInformation(
            "Watermark asset-fingerprint sweep started. Interval: {Seconds}s, batch: {Batch}",
            settings.BackgroundSweepIntervalSeconds, settings.SweepBatchSize);

        // Run immediately on startup so newly-flagged collections aren't waiting the full
        // interval before the first sweep — same pattern as TrashPurgeBackgroundService.
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Watermark sweep run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContextProvider = scope.ServiceProvider.GetRequiredService<DbContextProvider>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<WatermarkSettings>>().Value;

        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var db = lease.Db;

        // Eligible: (Asset.WatermarkOverride is true) OR (override is null AND ANY collection has WatermarkEnabled = true).
        // Not yet processed (or stale): AssetWatermarkAppliedAt is null OR < UpdatedAt.
        // Soft-deleted assets are filtered out by the global query filter.
        var candidates = await db.Assets
            .AsNoTracking()
            .Where(a => (a.AssetWatermarkAppliedAt == null || a.AssetWatermarkAppliedAt < a.UpdatedAt)
                     && (a.WatermarkOverride == true
                        || (a.WatermarkOverride == null
                            && a.AssetCollections.Any(ac => ac.Collection != null && ac.Collection.WatermarkEnabled))))
            .OrderBy(a => a.UpdatedAt)
            .Take(settings.SweepBatchSize)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            logger.LogDebug("Watermark sweep: no eligible assets");
            return;
        }

        var enqueued = 0;
        var failed = 0;
        foreach (var assetId in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await bus.PublishAsync(new EmbedAssetFingerprintCommand { AssetId = assetId });
                enqueued++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to enqueue asset-fingerprint command for {AssetId}", assetId);
                failed++;
            }
        }

        logger.LogInformation(
            "Watermark sweep enqueued {Enqueued} asset-fingerprint commands ({Failed} failed)",
            enqueued, failed);
    }
}
