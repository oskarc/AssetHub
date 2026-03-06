using AssetHub.Application.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Jobs;

/// <summary>
/// Recurring job that cleans up orphaned shares — shares that reference
/// assets or collections that no longer exist. This can occur if:
/// - An asset/collection is deleted via direct database manipulation
/// - A bug bypasses the service layer's cascade delete logic
/// - A migration script removes entities without cleaning up shares
/// </summary>
public class CleanupOrphanedSharesJob(
    IServiceScopeFactory scopeFactory,
    ILogger<CleanupOrphanedSharesJob> logger)
{
    public async Task ExecuteAsync()
    {
        logger.LogInformation("Starting orphaned shares cleanup");

        using var scope = scopeFactory.CreateScope();
        var shareRepo = scope.ServiceProvider.GetRequiredService<IShareRepository>();

        var deleted = await shareRepo.DeleteOrphanedAsync(CancellationToken.None);
            
        if (deleted > 0)
        {
            logger.LogInformation("Orphaned shares cleanup complete: {Deleted} shares removed", deleted);
        }
        else
        {
            logger.LogDebug("Orphaned shares cleanup complete: no orphaned shares found");
        }
    }
}
