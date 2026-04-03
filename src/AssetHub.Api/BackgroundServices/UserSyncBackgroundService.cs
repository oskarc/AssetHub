using AssetHub.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetHub.Api.BackgroundServices;

/// <summary>
/// Syncs deleted Keycloak users. Runs daily.
/// </summary>
public sealed class UserSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<UserSyncBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay — run first sync after 5 minutes to let the app fully start
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IUserSyncService>();
                await syncService.SyncDeletedUsersAsync(false, stoppingToken);
                logger.LogInformation("User sync completed successfully");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "User sync failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
