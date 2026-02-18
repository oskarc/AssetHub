using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dam.Worker.Jobs;

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

    public async Task ExecuteAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var assetRepo = scope.ServiceProvider.GetRequiredService<IAssetRepository>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAssetDeletionService>();
        var minioAdapter = scope.ServiceProvider.GetRequiredService<IMinIOAdapter>();
        var configuration = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        var bucketName = Dam.Application.Helpers.StorageConfig.GetBucketName(configuration);
        var cutoff = DateTime.UtcNow - StaleThreshold;

        logger.LogInformation("Starting stale upload cleanup (threshold: {Threshold})", StaleThreshold);

        var staleAssets = await assetRepo.GetByStatusAsync(
            Asset.StatusUploading, skip: 0, take: 500, CancellationToken.None);

        var cleaned = 0;
        foreach (var asset in staleAssets.Where(a => a.CreatedAt < cutoff))
        {
            try
            {
                await deletionService.PermanentDeleteAsync(asset, bucketName, CancellationToken.None);
                cleaned++;
                logger.LogInformation("Cleaned up stale upload: {AssetId} ({Title}, created {CreatedAt})",
                    asset.Id, asset.Title, asset.CreatedAt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up stale upload {AssetId}", asset.Id);
            }
        }

        logger.LogInformation("Stale upload cleanup complete: {Cleaned} assets removed out of {Total} checked",
            cleaned, staleAssets.Count);
    }
}
