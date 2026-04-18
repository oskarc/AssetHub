using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Thin orchestrator that schedules asset processing by publishing commands to the message broker.
/// Delegates actual processing to consumers that invoke
/// <see cref="ImageProcessingService"/> and <see cref="VideoProcessingService"/>.
/// </summary>
public sealed class MediaProcessingService(
    IAssetRepository assetRepository,
    IMessageBus messageBus,
    ILogger<MediaProcessingService> logger) : IMediaProcessingService
{
    public Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken cancellationToken = default)
        => ScheduleProcessingAsync(assetId, assetType, originalObjectKey, skipMetadata: false, cancellationToken);

    public async Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, bool skipMetadata, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid();

        if (assetType == Constants.AssetTypeFilters.Image)
        {
            logger.LogInformation("Publishing image processing command for asset {AssetId}, correlation {CorrelationId}", assetId, correlationId);
            await messageBus.PublishAsync(new ProcessImageCommand
            {
                AssetId = assetId,
                OriginalObjectKey = originalObjectKey,
                SkipMetadataExtraction = skipMetadata
            });
        }
        else if (assetType == Constants.AssetTypeFilters.Video)
        {
            logger.LogInformation("Publishing video processing command for asset {AssetId}, correlation {CorrelationId}", assetId, correlationId);
            await messageBus.PublishAsync(new ProcessVideoCommand
            {
                AssetId = assetId,
                OriginalObjectKey = originalObjectKey
            });
        }
        else
        {
            logger.LogInformation("No processing required for asset {AssetId} of type {AssetType}", assetId, assetType);
            // For documents and other types, mark as ready immediately
            var asset = await assetRepository.GetByIdAsync(assetId, cancellationToken);
            if (asset is not null)
            {
                asset.MarkReady();
                await assetRepository.UpdateAsync(asset, cancellationToken);
            }
        }

        return correlationId.ToString();
    }
}
