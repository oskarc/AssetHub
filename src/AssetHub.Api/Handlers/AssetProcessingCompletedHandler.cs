using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Api.Handlers;

public sealed class AssetProcessingCompletedHandler(
    IAssetRepository assetRepository,
    ILogger<AssetProcessingCompletedHandler> logger)
{
    public async Task HandleAsync(AssetProcessingCompletedEvent evt, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing completed for asset {AssetId}", evt.AssetId);

        var asset = await assetRepository.GetByIdAsync(evt.AssetId, cancellationToken);
        if (asset is null)
        {
            logger.LogWarning("Asset {AssetId} not found, skipping completion update", evt.AssetId);
            return;
        }

        asset.MarkReady(evt.ThumbObjectKey, evt.MediumObjectKey, evt.PosterObjectKey);
        ApplyAudioFields(asset, evt);

        if (evt.MetadataJson is not null)
        {
            asset.MetadataJson ??= new();
            foreach (var kvp in evt.MetadataJson)
            {
                asset.MetadataJson[kvp.Key] = kvp.Value;
            }
        }

        // Auto-populate Copyright field from extracted metadata if not already set
        if (string.IsNullOrWhiteSpace(asset.Copyright) && !string.IsNullOrWhiteSpace(evt.Copyright))
        {
            asset.Copyright = evt.Copyright;
            logger.LogInformation("Auto-populated Copyright for asset {AssetId} from EXIF: {Copyright}",
                evt.AssetId, evt.Copyright);
        }

        await assetRepository.UpdateAsync(asset, cancellationToken);
        logger.LogInformation("Asset {AssetId} marked as Ready with renditions", evt.AssetId);

    }

    /// <summary>
    /// Audio-only fields (T5-AUDIO-01) — null on image / video paths. The
    /// completion event carries everything the worker probed; we mirror it
    /// onto the row here rather than threading audio params through
    /// <see cref="Asset.MarkReady"/> itself.
    /// </summary>
    private static void ApplyAudioFields(Asset asset, AssetProcessingCompletedEvent evt)
    {
        if (evt.DurationSeconds is not null) asset.DurationSeconds = evt.DurationSeconds;
        if (evt.AudioBitrateKbps is not null) asset.AudioBitrateKbps = evt.AudioBitrateKbps;
        if (evt.AudioSampleRateHz is not null) asset.AudioSampleRateHz = evt.AudioSampleRateHz;
        if (evt.AudioChannels is not null) asset.AudioChannels = evt.AudioChannels;
        if (evt.WaveformPeaksPath is not null) asset.WaveformPeaksPath = evt.WaveformPeaksPath;
    }
}
