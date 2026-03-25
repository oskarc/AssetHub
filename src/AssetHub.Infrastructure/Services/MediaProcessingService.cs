using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Thin orchestrator that schedules asset processing jobs based on asset type.
/// Delegates actual processing to <see cref="ImageProcessingService"/> and <see cref="VideoProcessingService"/>.
/// </summary>
public sealed class MediaProcessingService(
    IAssetRepository assetRepository,
    IBackgroundJobClient backgroundJobClient,
    ILogger<MediaProcessingService> logger) : IMediaProcessingService
{
    public async Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken cancellationToken = default)
    {
        string jobId;

        if (assetType == Constants.AssetTypeFilters.Image)
        {
            logger.LogInformation("Scheduling image processing for asset {AssetId}", assetId);
            jobId = backgroundJobClient.Enqueue<ImageProcessingService>(x => x.ProcessImageAsync(assetId, originalObjectKey, CancellationToken.None));
        }
        else if (assetType == Constants.AssetTypeFilters.Video)
        {
            logger.LogInformation("Scheduling video processing for asset {AssetId}", assetId);
            jobId = backgroundJobClient.Enqueue<VideoProcessingService>(x => x.ProcessVideoAsync(assetId, originalObjectKey, CancellationToken.None));
        }
        else
        {
            logger.LogInformation("No processing required for asset {AssetId} of type {AssetType}", assetId, assetType);
            // For documents and other types, mark as ready immediately
            var asset = await assetRepository.GetByIdAsync(assetId, cancellationToken);
            if (asset != null)
            {
                asset.MarkReady();
                await assetRepository.UpdateAsync(asset, cancellationToken);
            }
            jobId = "no-processing-required";
        }

        return jobId;
    }
}
