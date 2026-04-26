using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Cron loop that triggers <see cref="IAuditRetentionSweeper"/> every
/// <c>AuditRetentionSettings.SweepIntervalSeconds</c>. The sweep itself lives
/// in <c>AuditRetentionSweeper</c> in the Infrastructure layer so integration
/// tests can drive a sweep directly without spinning up the BackgroundService
/// loop (T5-AUDIT-01).
/// </summary>
public sealed class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditRetentionSettings> settings,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var s = settings.Value;
        var interval = TimeSpan.FromSeconds(s.SweepIntervalSeconds);
        logger.LogInformation(
            "Audit retention worker started. Default {Default} d, {Overrides} per-event override(s), interval {Interval} s, batch {Batch}",
            s.DefaultRetentionDays, s.PerEventTypeOverrides.Count, s.SweepIntervalSeconds, s.BatchSize);

        // Run on every tick; PeriodicTimer fires after the first interval so
        // the first sweep happens after `interval` has elapsed since startup.
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<IAuditRetentionSweeper>();
                await sweeper.SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Audit retention sweep failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
