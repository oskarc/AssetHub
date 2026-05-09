using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Hourly cron loop that drives <see cref="IAnalyticsRollupService"/>
/// (T5-ANL-01). Two work passes:
/// <list type="number">
///   <item>On first start-up, back-fill the last <c>BackfillDays</c> of UTC
///   days that don't yet have a rollup row.</item>
///   <item>Every hour after that, roll up "yesterday" if it isn't already
///   in the table — self-heal in case the configured cron hour was missed.</item>
/// </list>
/// Idempotency comes from <see cref="IAnalyticsRollupService.RollupDayAsync"/>:
/// re-running for the same day overwrites the row, so concurrent / repeat
/// ticks can't double-count.
/// </summary>
public sealed class AnalyticsRollupBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<AnalyticsSettings> settings,
    ILogger<AnalyticsRollupBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var s = settings.Value;
        if (!s.Enabled)
        {
            logger.LogInformation("Analytics rollup disabled via Analytics:Enabled = false");
            return;
        }

        logger.LogInformation(
            "Analytics rollup worker started. Cron {Hour:00}:{Minute:00} UTC, back-fill {BackfillDays} d",
            s.CronHour, s.CronMinute, s.BackfillDays);

        // Back-fill on first run (best-effort — failure logged, doesn't block forward rollup).
        try
        {
            await BackfillAsync(s.BackfillDays, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Analytics rollup back-fill failed");
        }

        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Analytics rollup tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Find every UTC day in the last <paramref name="backfillDays"/> that doesn't
    /// already have a rollup row, and run <see cref="IAnalyticsRollupService.RollupDayAsync"/>
    /// for each. Skips days that are already complete so a worker restart doesn't
    /// re-roll a month of data on every boot.
    /// </summary>
    private async Task BackfillAsync(int backfillDays, CancellationToken ct)
    {
        if (backfillDays <= 0) return;

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var rollup = sp.GetRequiredService<IAnalyticsRollupService>();
        var dbContextProvider = sp.GetRequiredService<DbContextProvider>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = today.AddDays(-backfillDays);

        // Probe existing days once so the per-day decision stays cheap.
        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var existing = await lease.Db.AnalyticsDailyRollups
            .AsNoTracking()
            .Where(r => r.Date >= earliest && r.Date < today)
            .Select(r => r.Date)
            .Distinct()
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var rolled = 0;
        for (var day = earliest; day < today; day = day.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            if (existingSet.Contains(day)) continue;
            var result = await rollup.RollupDayAsync(day, ct);
            if (result.IsSuccess) rolled++;
        }

        logger.LogInformation(
            "Analytics rollup back-fill complete: {Rolled} day(s) rolled, {Skipped} already present",
            rolled, existingSet.Count);
    }

    /// <summary>
    /// Hourly tick — roll up yesterday if it isn't in the table yet. This is
    /// the self-heal path for missed cron windows. The "right" run-time is
    /// just-after-midnight UTC; we don't gate on the cron hour because doing
    /// so creates a 24-hour blind spot if the worker pod is restarting at
    /// 02:00 UTC every day.
    /// </summary>
    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var rollup = sp.GetRequiredService<IAnalyticsRollupService>();
        var dbContextProvider = sp.GetRequiredService<DbContextProvider>();

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        await using var lease = await dbContextProvider.AcquireAsync(ct);
        var alreadyRolled = await lease.Db.AnalyticsDailyRollups
            .AsNoTracking()
            .AnyAsync(r => r.Date == yesterday, ct);

        if (alreadyRolled)
        {
            logger.LogDebug("Analytics rollup tick — {Day} already rolled, skipping", yesterday);
            return;
        }

        var result = await rollup.RollupDayAsync(yesterday, ct);
        if (result.IsSuccess)
        {
            logger.LogInformation("Analytics rollup tick — rolled {Day}", yesterday);
        }
        else
        {
            logger.LogWarning("Analytics rollup tick — rollup for {Day} returned error: {Error}",
                yesterday, result.Error?.Message);
        }
    }
}

/// <summary>
/// Hourly sweep that deletes expired analytics PDF jobs + their MinIO objects
/// (T5-ANL-01). Mirrors <c>ZipCleanupBackgroundService</c> shape.
/// </summary>
public sealed class AnalyticsPdfCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<AnalyticsPdfCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                var jobRepo = sp.GetRequiredService<IAnalyticsPdfJobRepository>();
                var minio = sp.GetRequiredService<IMinIOAdapter>();
                var settings = sp.GetRequiredService<IOptions<AssetHub.Application.Configuration.MinIOSettings>>();
                var bucket = settings.Value.BucketName;
                var dbContextProvider = sp.GetRequiredService<DbContextProvider>();

                var now = DateTime.UtcNow;

                // Per-row delete-then-purge so we know which keys to remove.
                await using var lease = await dbContextProvider.AcquireAsync(stoppingToken);
                var expired = await lease.Db.AnalyticsPdfJobs
                    .Where(j => j.ExpiresAt < now && j.ObjectKey != null)
                    .Select(j => j.ObjectKey!)
                    .ToListAsync(stoppingToken);

                foreach (var key in expired)
                {
                    try
                    {
                        await minio.DeleteAsync(bucket, key, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        // MinIO failure is non-fatal — orphan-objects sweeper picks it up.
                        logger.LogWarning(ex, "Failed to delete expired analytics PDF object {Key}", key);
                    }
                }

                var deleted = await jobRepo.DeleteExpiredAsync(now, stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Analytics PDF cleanup deleted {Deleted} expired job row(s)", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Analytics PDF cleanup tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
