using AssetHub.Application.Messages;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AssetHub.Worker.Handlers;

public sealed class ProcessAudioHandler(
    AudioProcessingService audioProcessingService,
    ILogger<ProcessAudioHandler> logger)
{
    public async Task<object[]> HandleAsync(ProcessAudioCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Received audio processing command for asset {AssetId}", command.AssetId);

        var result = await audioProcessingService.ProcessAudioAsync(
            command.AssetId, command.OriginalObjectKey, cancellationToken);

        if (result.Succeeded)
        {
            logger.LogInformation("Publishing processing completed event for asset {AssetId}", command.AssetId);
            return [new AssetProcessingCompletedEvent
            {
                AssetId = command.AssetId,
                DurationSeconds = result.DurationSeconds,
                AudioBitrateKbps = result.AudioBitrateKbps,
                AudioSampleRateHz = result.AudioSampleRateHz,
                AudioChannels = result.AudioChannels,
                WaveformPeaksPath = result.WaveformPeaksPath
            }];
        }

        logger.LogWarning("Publishing processing failed event for asset {AssetId}: {Error}", command.AssetId, result.ErrorMessage);
        return [new AssetProcessingFailedEvent
        {
            AssetId = command.AssetId,
            ErrorMessage = result.ErrorMessage ?? "Unknown error",
            ErrorType = result.ErrorType ?? "Unknown",
            AssetType = "audio"
        }];
    }
}
