using AssetHub.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetHub.Api.BackgroundServices;

/// <summary>
/// Cleans up expired ZIP download files from MinIO.
/// Runs every hour.
/// </summary>
public sealed class ZipCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ZipCleanupBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay — run first cleanup after 2 minutes
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var zipService = scope.ServiceProvider.GetRequiredService<IZipBuildService>();
                await zipService.CleanupExpiredAsync(stoppingToken);
                logger.LogDebug("ZIP cleanup completed");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ZIP cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
