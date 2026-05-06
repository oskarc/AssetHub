using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Watermarking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.Handlers;

/// <summary>
/// Background handler that embeds the asset-fingerprint layer into an asset's stored
/// MinIO renditions (T5-WMK-01). Idempotent: handler reads the asset's current state,
/// regenerates the fingerprint, replaces each rendition object, and stamps
/// <c>AssetWatermarkAppliedAt</c>. Re-enqueued whenever <c>AssetWatermarkAppliedAt</c>
/// is null or older than <c>UpdatedAt</c>.
///
/// Pure perf optimisation per the no-gap rule — downloads always emit a fully two-
/// layer file, but pre-baking the asset-fingerprint here means each on-the-fly
/// download does only the recipient layer instead of both.
/// </summary>
public sealed class EmbedAssetFingerprintHandler(
    IAssetRepository assetRepository,
    IMinIOAdapter minioAdapter,
    IWatermarkEmbedder embedder,
    IAuditService auditService,
    IOptions<MinIOSettings> minioSettings,
    ILogger<EmbedAssetFingerprintHandler> logger)
{
    public async Task HandleAsync(EmbedAssetFingerprintCommand command, CancellationToken ct)
    {
        var asset = await assetRepository.GetByIdAsync(command.AssetId, ct);
        if (asset is null)
        {
            logger.LogDebug("Asset {AssetId} not found for asset-fingerprint embed (skipping)", command.AssetId);
            return;
        }

        // Generate token if it hasn't been set yet (sweep is the canonical path).
        if (asset.AssetWatermarkToken is null)
        {
            asset.AssetWatermarkToken = Guid.NewGuid().ToString("D");
        }

        var bucket = minioSettings.Value.BucketName;
        var keys = new (string Description, string? Key)[]
        {
            ("original", asset.OriginalObjectKey),
            ("medium", asset.MediumObjectKey),
            ("thumb", asset.ThumbObjectKey)
        };

        var processed = 0;
        foreach (var (description, key) in keys)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            try
            {
                await EmbedOneRenditionAsync(bucket, key, asset.AssetWatermarkToken, description, ct);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-rendition try/catch: a malformed thumb shouldn't poison the original.
                logger.LogWarning(ex,
                    "Failed to embed asset-fingerprint into {Description} ({Key}) for asset {AssetId}",
                    description, key, asset.Id);
            }
        }

        if (processed == 0)
        {
            logger.LogWarning(
                "No renditions processed for asset {AssetId} — refusing to stamp AssetWatermarkAppliedAt",
                asset.Id);
            return;
        }

        asset.AssetWatermarkAppliedAt = DateTime.UtcNow;
        await assetRepository.UpdateAsync(asset, ct);

        await auditService.LogAsync(
            eventType: WatermarkConstants.AuditEvents.Embedded,
            targetType: Constants.ScopeTypes.Asset,
            targetId: asset.Id,
            actorUserId: null,
            details: new Dictionary<string, object>
            {
                ["asset_watermark_token"] = asset.AssetWatermarkToken,
                ["renditions_processed"] = processed,
                ["algorithm_version"] = embedder.AlgorithmVersion
            },
            ct: ct);

        logger.LogInformation(
            "Embedded asset-fingerprint into asset {AssetId} ({Count} renditions)",
            asset.Id, processed);
    }

    private async Task EmbedOneRenditionAsync(
        string bucket, string key, string assetWatermarkToken, string description, CancellationToken ct)
    {
        // Download → embed → upload back (overwrites). The MinIO write replaces the
        // canonical rendition object so subsequent downloads serve the watermarked bytes.
        Stream original;
        await using (var src = await minioAdapter.DownloadAsync(bucket, key, ct))
        {
            original = new MemoryStream();
            await src.CopyToAsync(original, ct);
            original.Position = 0;
        }

        var payload = new AssetFingerprintPayload(
            AssetWatermarkToken: assetWatermarkToken,
            AlgorithmVersion: embedder.AlgorithmVersion);
        await using var watermarked = await embedder.EmbedAssetFingerprintAsync(original, payload, ct);

        await minioAdapter.UploadAsync(bucket, key, watermarked, "image/png", ct);

        await original.DisposeAsync();

        logger.LogDebug(
            "Re-uploaded watermarked {Description} for key {Key} ({Bytes} bytes original)",
            description, key, original.Length);
    }
}
