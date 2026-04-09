using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
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
}
