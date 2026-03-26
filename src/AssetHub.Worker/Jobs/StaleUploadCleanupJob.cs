using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.Jobs;

/// <summary>
/// Recurring job that cleans up assets stuck in "uploading" status for too long.
/// These are typically from abandoned presigned uploads where the user navigated away
/// before completing the upload+confirm flow.
/// </summary>
public class StaleUploadCleanupJob(
    IServiceScopeFactory scopeFactory,
    ILogger<StaleUploadCleanupJob> logger)
{
    /// <summary>
    /// Default threshold: assets in "uploading" status older than this are considered stale.
    /// </summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(24);

    public async Task ExecuteAsync(CancellationToken ct)
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

        do
        {
            var staleAssets = await assetRepo.GetByStatusAsync(
                AssetStatus.Uploading.ToDbString(), skip: 0, take: batchSize, ct);

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
        } while (batchCleaned > 0);

        logger.LogInformation("Stale upload cleanup complete: {Cleaned} assets removed", cleaned);
    }
}
