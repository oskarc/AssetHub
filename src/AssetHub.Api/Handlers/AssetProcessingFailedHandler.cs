using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;

namespace AssetHub.Api.Handlers;

public sealed class AssetProcessingFailedHandler(
    IAssetRepository assetRepository,
    IAuditService auditService,
    ILogger<AssetProcessingFailedHandler> logger)
{
    public async Task HandleAsync(AssetProcessingFailedEvent evt, CancellationToken cancellationToken)
    {
        logger.LogWarning("Processing failed for asset {AssetId}: {Error}", evt.AssetId, evt.ErrorMessage);

        var asset = await assetRepository.GetByIdAsync(evt.AssetId, cancellationToken);
        if (asset != null)
        {
            var typeLabel = string.IsNullOrEmpty(evt.AssetType) ? "Asset" : $"{char.ToUpper(evt.AssetType[0])}{evt.AssetType[1..]}";
            asset.MarkFailed($"{typeLabel} processing failed. Please try uploading again or contact an administrator.");
            await assetRepository.UpdateAsync(asset, cancellationToken);
            logger.LogInformation("Asset {AssetId} marked as Failed", evt.AssetId);
        }
        else
        {
            logger.LogWarning("Asset {AssetId} not found, skipping failure update", evt.AssetId);
        }

        await auditService.LogAsync(
            "asset.processing_failed",
            Constants.ScopeTypes.Asset,
            evt.AssetId,
            actorUserId: null,
            new Dictionary<string, object>
            {
                ["assetType"] = evt.AssetType,
                ["error"] = evt.ErrorMessage,
                ["errorType"] = evt.ErrorType
            },
            cancellationToken);
    }
}
