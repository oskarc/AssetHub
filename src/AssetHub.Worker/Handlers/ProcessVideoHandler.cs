using AssetHub.Application.Messages;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AssetHub.Worker.Handlers;

public sealed class ProcessVideoHandler(
    VideoProcessingService videoProcessingService,
    ILogger<ProcessVideoHandler> logger)
{
    public async Task<object[]> HandleAsync(ProcessVideoCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Received video processing command for asset {AssetId}", command.AssetId);

        var result = await videoProcessingService.ProcessVideoAsync(
            command.AssetId, command.OriginalObjectKey, cancellationToken);

        if (result.Succeeded)
        {
            logger.LogInformation("Publishing processing completed event for asset {AssetId}", command.AssetId);
            return [new AssetProcessingCompletedEvent
            {
                AssetId = command.AssetId,
                ThumbObjectKey = result.ThumbObjectKey,
                PosterObjectKey = result.PosterObjectKey
            }];
        }

        logger.LogWarning("Publishing processing failed event for asset {AssetId}: {Error}", command.AssetId, result.ErrorMessage);
        return [new AssetProcessingFailedEvent
        {
            AssetId = command.AssetId,
            ErrorMessage = result.ErrorMessage ?? "Unknown error",
            ErrorType = result.ErrorType ?? "Unknown",
            AssetType = "video"
        }];
    }
}
