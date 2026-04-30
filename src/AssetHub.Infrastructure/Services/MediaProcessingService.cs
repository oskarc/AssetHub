using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Thin orchestrator that schedules asset processing by publishing commands to the message broker.
/// Delegates actual processing to consumers that invoke
/// <see cref="ImageProcessingService"/> and <see cref="VideoProcessingService"/>.
/// </summary>
/// <remarks>
/// Publishes go through <see cref="IOutboxPublisher"/> so a Rabbit blip
/// between the upload commit and the broker enqueue can't strand the asset
/// in <c>Pending</c> forever — the row joins the caller's transaction (when
/// one is open) and the OutboxDrainService delivers to Rabbit out-of-band (D-2).
/// </remarks>
public sealed class MediaProcessingService(
    IAssetRepository assetRepository,
    IOutboxPublisher outbox,
    IWebhookEventPublisher webhooks,
    ILogger<MediaProcessingService> logger) : IMediaProcessingService
{
    public Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken cancellationToken = default)
        => ScheduleProcessingAsync(assetId, assetType, originalObjectKey, skipMetadata: false, cancellationToken);

    public async Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, bool skipMetadata, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid();

        if (assetType == Constants.AssetTypeFilters.Image)
        {
            logger.LogInformation("Enqueueing image processing command for asset {AssetId}, correlation {CorrelationId}", assetId, correlationId);
            await outbox.EnqueueAsync(new ProcessImageCommand
            {
                AssetId = assetId,
                OriginalObjectKey = originalObjectKey,
                SkipMetadataExtraction = skipMetadata
            }, cancellationToken);
        }
        else if (assetType == Constants.AssetTypeFilters.Video)
        {
            logger.LogInformation("Enqueueing video processing command for asset {AssetId}, correlation {CorrelationId}", assetId, correlationId);
            await outbox.EnqueueAsync(new ProcessVideoCommand
            {
                AssetId = assetId,
                OriginalObjectKey = originalObjectKey
            }, cancellationToken);
        }
        else if (assetType == Constants.AssetTypeFilters.Audio)
        {
            logger.LogInformation("Enqueueing audio processing command for asset {AssetId}, correlation {CorrelationId}", assetId, correlationId);
            await outbox.EnqueueAsync(new ProcessAudioCommand
            {
                AssetId = assetId,
                OriginalObjectKey = originalObjectKey
            }, cancellationToken);
        }
        else
        {
            logger.LogInformation("No processing required for asset {AssetId} of type {AssetType}", assetId, assetType);
            // For documents and other types, mark as ready immediately. The
            // image / video paths emit asset.created in
            // AssetProcessingCompletedHandler after the worker pipeline; this
            // synchronous branch has to emit the equivalent event itself.
            var asset = await assetRepository.GetByIdAsync(assetId, cancellationToken);
            if (asset is not null)
            {
                asset.MarkReady();
                await assetRepository.UpdateAsync(asset, cancellationToken);

                await webhooks.PublishAsync(WebhookEvents.AssetCreated, new
                {
                    assetId = asset.Id,
                    title = asset.Title,
                    assetType = asset.AssetType.ToDbString(),
                    contentType = asset.ContentType,
                    sizeBytes = asset.SizeBytes,
                    createdAt = asset.CreatedAt,
                    createdByUserId = asset.CreatedByUserId
                }, cancellationToken);
            }
        }

        return correlationId.ToString();
    }
}
