using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Handlers;

/// <summary>
/// Handles export preset application for edited images.
/// Downloads the source image, resizes per preset dimensions via ImageMagick,
/// uploads each derivative, and triggers standard image processing for thumbnails.
/// </summary>
public sealed class ApplyExportPresetsHandler(
    IAssetRepository assetRepo,
    IExportPresetRepository presetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    ImageProcessingService imageProcessingService,
    IMediaProcessingService mediaProcessingService,
    IAuditService auditService,
    ILogger<ApplyExportPresetsHandler> logger)
{
    public async Task HandleAsync(ApplyExportPresetsCommand command, CancellationToken ct)
    {
        logger.LogInformation(
            "Applying {PresetCount} export preset(s) to asset {SourceAssetId} requested by {UserId}",
            command.PresetIds.Count, command.SourceAssetId, command.RequestedByUserId);

        var sourceAsset = await assetRepo.GetByIdAsync(command.SourceAssetId, ct);
        if (sourceAsset is null)
        {
            logger.LogWarning("Source asset {SourceAssetId} not found, skipping preset application",
                command.SourceAssetId);
            return;
        }

        var presets = await presetRepo.GetByIdsAsync(command.PresetIds, ct);
        if (presets.Count == 0)
        {
            logger.LogWarning("No valid presets found for IDs {PresetIds}, skipping",
                string.Join(", ", command.PresetIds));
            return;
        }

        // Get the collections the source belongs to so derivatives can be linked
        var collectionIds = await assetCollectionRepo.GetCollectionIdsForAssetAsync(command.SourceAssetId, ct);

        var derivativeCount = 0;
        foreach (var preset in presets)
        {
            try
            {
                await CreatePresetDerivativeAsync(sourceAsset, preset, collectionIds, command.RequestedByUserId, ct);
                derivativeCount++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to create derivative for preset {PresetId} '{PresetName}' from asset {AssetId}",
                    preset.Id, preset.Name, sourceAsset.Id);
            }
        }

        logger.LogInformation(
            "Created {Count}/{Total} derivative(s) for asset {AssetId}",
            derivativeCount, presets.Count, command.SourceAssetId);
    }

    private static readonly Dictionary<string, (string Extension, string ContentType)> FormatMap = new()
    {
        ["jpeg"] = (".jpg", "image/jpeg"),
        ["png"] = (".png", "image/png"),
        ["webp"] = (".webp", "image/webp"),
    };

    private async Task CreatePresetDerivativeAsync(
        Asset sourceAsset, ExportPreset preset, List<Guid> collectionIds,
        string requestedByUserId, CancellationToken ct)
    {
        var format = preset.Format == ExportPresetFormat.Original ? "png" : preset.Format.ToDbString();
        var (extension, contentType) = FormatMap.TryGetValue(format, out var mapping)
            ? mapping
            : (".png", "image/png");

        var derivativeId = Guid.NewGuid();
        var objectKey = $"originals/{derivativeId}-{preset.Name.ToLowerInvariant().Replace(' ', '-')}{extension}";
        var title = $"{sourceAsset.Title} ({preset.Name})";

        // Create the derivative asset record
        var derivative = new Asset
        {
            Id = derivativeId,
            Title = title,
            ContentType = contentType,
            AssetType = AssetType.Image,
            Status = AssetStatus.Processing,
            OriginalObjectKey = objectKey,
            SizeBytes = 0, // Will be updated after processing
            SourceAssetId = sourceAsset.Id,
            Tags = new List<string>(sourceAsset.Tags),
            MetadataJson = new Dictionary<string, object>
            {
                ["presetId"] = preset.Id.ToString(),
                ["presetName"] = preset.Name,
                ["targetWidth"] = preset.Width?.ToString() ?? "auto",
                ["targetHeight"] = preset.Height?.ToString() ?? "auto",
                ["fitMode"] = preset.FitMode.ToDbString(),
                ["format"] = preset.Format.ToDbString(),
                ["quality"] = preset.Quality.ToString()
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = requestedByUserId
        };

        await assetRepo.CreateAsync(derivative, ct);

        // Link to same collections as source
        foreach (var collectionId in collectionIds)
        {
            try
            {
                await assetCollectionRepo.AddToCollectionAsync(derivativeId, collectionId, requestedByUserId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to link derivative {DerivativeId} to collection {CollectionId}",
                    derivativeId, collectionId);
            }
        }

        // Download source, resize per preset dimensions via ImageMagick, then upload
        var sizeBytes = await imageProcessingService.ResizeForPresetAsync(
            sourceAsset.OriginalObjectKey, objectKey, contentType,
            preset, ct);

        derivative.SizeBytes = sizeBytes;
        await assetRepo.UpdateAsync(derivative, ct);

        // Dispatch standard image processing for thumbnail/medium generation
        await mediaProcessingService.ScheduleProcessingAsync(
            derivative.Id, derivative.AssetType.ToDbString(), derivative.OriginalObjectKey, ct);

        await auditService.LogAsync("asset.exported_with_preset", "asset", derivativeId,
            requestedByUserId, new Dictionary<string, object>
            {
                ["sourceAssetId"] = sourceAsset.Id,
                ["presetId"] = preset.Id,
                ["presetName"] = preset.Name
            }, ct);

        logger.LogInformation(
            "Created derivative asset {DerivativeId} for preset '{PresetName}' from asset {SourceId}",
            derivativeId, preset.Name, sourceAsset.Id);
    }
}
