using AssetHub.Application.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Cleans up orphaned shares — shares referencing deleted assets or collections.
/// Runs weekly on Sundays at approximately 4:00 AM UTC.
/// </summary>
public sealed class OrphanedSharesCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<OrphanedSharesCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until roughly 4:00 AM UTC on first run
        var now = DateTime.UtcNow;
        var nextRun = now.Date.AddHours(4);
        if (nextRun <= now) nextRun = nextRun.AddDays(1);
        var initialDelay = nextRun - now;

        logger.LogInformation("Orphaned shares cleanup scheduled, first run in {Delay}", initialDelay);
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
                logger.LogError(ex, "Orphaned shares cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting orphaned shares cleanup");

        using var scope = scopeFactory.CreateScope();
        var shareRepo = scope.ServiceProvider.GetRequiredService<IShareRepository>();

        var deleted = await shareRepo.DeleteOrphanedAsync(ct);

        if (deleted > 0)
            logger.LogInformation("Orphaned shares cleanup complete: {Deleted} shares removed", deleted);
        else
            logger.LogDebug("Orphaned shares cleanup complete: no orphaned shares found");
    }
}
