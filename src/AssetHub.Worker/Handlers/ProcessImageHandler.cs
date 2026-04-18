using AssetHub.Application.Messages;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AssetHub.Worker.Handlers;

public sealed class ProcessImageHandler(
    ImageProcessingService imageProcessingService,
    ILogger<ProcessImageHandler> logger)
{
    public async Task<object[]> HandleAsync(ProcessImageCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Received image processing command for asset {AssetId}", command.AssetId);

        var result = await imageProcessingService.ProcessImageAsync(
            command.AssetId, command.OriginalObjectKey, command.SkipMetadataExtraction, cancellationToken);

        if (result.Succeeded)
        {
            logger.LogInformation("Publishing processing completed event for asset {AssetId}", command.AssetId);
            return [new AssetProcessingCompletedEvent
            {
                AssetId = command.AssetId,
                ThumbObjectKey = result.ThumbObjectKey,
                MediumObjectKey = result.MediumObjectKey,
                MetadataJson = result.Metadata,
                Copyright = result.Copyright
            }];
        }

        logger.LogWarning("Publishing processing failed event for asset {AssetId}: {Error}", command.AssetId, result.ErrorMessage);
        return [new AssetProcessingFailedEvent
        {
            AssetId = command.AssetId,
            ErrorMessage = result.ErrorMessage ?? "Unknown error",
            ErrorType = result.ErrorType ?? "Unknown",
            AssetType = "image"
        }];
    }
}
