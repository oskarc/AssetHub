using AssetHub.Application;
using AssetHub.Application.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Jobs;

/// <summary>
/// Recurring job that deletes audit events older than <see cref="Constants.Limits.AuditRetentionDays"/> days.
/// Prevents unbounded growth of the audit_events table in long-running production deployments.
/// </summary>
public class AuditRetentionJob(
    IServiceScopeFactory scopeFactory,
    ILogger<AuditRetentionJob> logger)
{
    public async Task ExecuteAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-Constants.Limits.AuditRetentionDays);
        logger.LogInformation(
            "Starting audit retention cleanup (retaining {Days} days, cutoff: {Cutoff:O})",
            Constants.Limits.AuditRetentionDays, cutoff);

        using var scope = scopeFactory.CreateScope();
        var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditEventRepository>();

        try
        {
            var deleted = await auditRepo.DeleteOlderThanAsync(cutoff, CancellationToken.None);

            if (deleted > 0)
                logger.LogInformation("Audit retention cleanup complete: {Deleted} events removed", deleted);
            else
                logger.LogDebug("Audit retention cleanup complete: no events older than {Days} days found",
                    Constants.Limits.AuditRetentionDays);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during audit retention cleanup");
            throw;
        }
    }
}
