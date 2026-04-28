using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Orchestrates image edit operations: replace, copy, and copy-with-presets.
/// Delegates storage operations to <see cref="IAssetUploadService"/> and
/// queues preset generation to the worker via Wolverine.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "DI constructor — all dependencies are distinct concerns")]
public sealed class ImageEditingService(
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    IExportPresetRepository presetRepo,
    IAssetUploadService uploadService,
    IMinIOAdapter minioAdapter,
    ICollectionAuthorizationService authService,
    IAuditService auditService,
    IMessageBus messageBus,
    CurrentUser currentUser,
    IOptions<MinIOSettings> minioSettings,
    ILogger<ImageEditingService> logger) : IImageEditingService
{
    private const string PngContentType = "image/png";
    private readonly string _bucketName = minioSettings.Value.BucketName;

    public async Task<ServiceResult<ImageEditResultDto>> ApplyEditAsync(
        Guid sourceAssetId, ImageEditRequestDto dto, Stream renderedPng, string fileName,
        long fileSize, CancellationToken ct)
    {
        var sourceAsset = await assetRepo.GetByIdAsync(sourceAssetId, ct);
        if (sourceAsset is null)
            return ServiceError.NotFound("Source asset not found");

        if (sourceAsset.AssetType != AssetType.Image)
            return ServiceError.BadRequest("Only images can be edited");

        // Verify user has access to at least one collection containing the source asset
        var collectionIds = await assetCollectionRepo.GetCollectionIdsForAssetAsync(sourceAssetId, ct);
        var requiredRole = dto.SaveMode == ImageEditSaveMode.Replace ? "manager" : "contributor";
        if (!currentUser.IsSystemAdmin)
        {
            if (collectionIds.Count == 0)
                return ServiceError.Forbidden("You do not have permission to edit this asset");

            var accessible = await authService.FilterAccessibleAsync(
                currentUser.UserId!, collectionIds, requiredRole, ct);
            if (accessible.Count == 0)
            {
                var message = dto.SaveMode == ImageEditSaveMode.Replace
                    ? "Replace requires manager permissions"
                    : "You do not have permission to edit this asset";
                return ServiceError.Forbidden(message);
            }
        }

        return dto.SaveMode switch
        {
            ImageEditSaveMode.Replace => await HandleReplaceAsync(sourceAsset, dto, renderedPng, fileSize, ct),
            ImageEditSaveMode.Copy => await HandleCopyAsync(sourceAsset, dto, renderedPng, fileSize, ct),
            ImageEditSaveMode.CopyWithPresets => await HandleCopyWithPresetsAsync(sourceAsset, dto, renderedPng, fileSize, ct),
            _ => ServiceError.BadRequest($"Unknown save mode: {dto.SaveMode}")
        };
    }

    private async Task<ServiceResult<ImageEditResultDto>> HandleReplaceAsync(
        Asset sourceAsset, ImageEditRequestDto dto, Stream renderedPng,
        long fileSize, CancellationToken ct)
    {
        // No ClamAV scan needed — the PNG is rendered by the browser's canvas.toBlob() API
        // from an already-scanned source image. Canvas output is pure pixel data.
        var replaceResult = await uploadService.ReplaceImageFileAsync(sourceAsset.Id, new ReplaceImageFileRequest
        {
            ContentType = PngContentType,
            FileSize = fileSize
        }, ct);

        if (!replaceResult.IsSuccess)
            return replaceResult.Error!;

        // Upload the rendered PNG directly to MinIO
        var objectKey = replaceResult.Value!.ObjectKey;
        await minioAdapter.UploadAsync(_bucketName, objectKey, renderedPng, PngContentType, ct);

        // Save edit document if provided. Re-fetch the asset first — the
        // `sourceAsset` we have in this scope was loaded BEFORE
        // ReplaceImageFileAsync mutated the row (new ObjectKey, Status=Uploading,
        // CurrentVersionNumber++). Calling UpdateAsync on the stale instance
        // would clobber those changes back to the pre-replace state and break
        // ConfirmPreScannedUploadAsync's `Status == Uploading` invariant.
        if (dto.EditDocument is not null)
        {
            var fresh = await assetRepo.GetByIdAsync(sourceAsset.Id, ct);
            if (fresh is not null)
            {
                fresh.EditDocument = dto.EditDocument;
                await assetRepo.UpdateAsync(fresh, ct);
            }
        }

        // Confirm — skip scan (canvas-rendered) and metadata extraction (no EXIF in edited PNGs)
        var confirmResult = await uploadService.ConfirmPreScannedUploadAsync(sourceAsset.Id, skipMetadata: true, ct);
        if (!confirmResult.IsSuccess)
            return confirmResult.Error!;

        logger.LogInformation("Image {AssetId} replaced by {UserId}", sourceAsset.Id, currentUser.UserId);

        await auditService.LogAsync("asset.edited", "asset", sourceAsset.Id, currentUser.UserId,
            new Dictionary<string, object> { ["saveMode"] = "replace" }, ct);

        return new ImageEditResultDto { AssetId = sourceAsset.Id };
    }

    private async Task<ServiceResult<ImageEditResultDto>> HandleCopyAsync(
        Asset sourceAsset, ImageEditRequestDto dto, Stream renderedPng,
        long fileSize, CancellationToken ct)
    {
        // No ClamAV scan needed — the PNG is rendered by the browser's canvas.toBlob() API
        // from an already-scanned source image. Canvas output is pure pixel data.
        var copyResult = await uploadService.SaveImageCopyAsync(sourceAsset.Id, new SaveImageCopyRequest
        {
            ContentType = PngContentType,
            FileSize = fileSize,
            Title = dto.Title,
            CollectionId = dto.DestinationCollectionId
        }, ct);

        if (!copyResult.IsSuccess)
            return copyResult.Error!;

        var newAssetId = copyResult.Value!.AssetId;
        var objectKey = copyResult.Value!.ObjectKey;

        // Upload the rendered PNG directly to MinIO
        await minioAdapter.UploadAsync(_bucketName, objectKey, renderedPng, PngContentType, ct);

        // Set parent lineage and edit document
        var newAsset = await assetRepo.GetByIdAsync(newAssetId, ct);
        if (newAsset is not null)
        {
            newAsset.SourceAssetId = sourceAsset.Id;
            if (dto.EditDocument is not null)
                newAsset.EditDocument = dto.EditDocument;
            await assetRepo.UpdateAsync(newAsset, ct);
        }

        // Confirm — skip scan (canvas-rendered) and metadata extraction (no EXIF in edited PNGs)
        var confirmResult = await uploadService.ConfirmPreScannedUploadAsync(newAssetId, skipMetadata: true, ct);
        if (!confirmResult.IsSuccess)
            return confirmResult.Error!;

        logger.LogInformation("Image copy {NewAssetId} created from {SourceId} by {UserId}",
            newAssetId, sourceAsset.Id, currentUser.UserId);

        await auditService.LogAsync("asset.edited", "asset", newAssetId, currentUser.UserId,
            new Dictionary<string, object>
            {
                ["saveMode"] = "copy",
                ["sourceAssetId"] = sourceAsset.Id
            }, ct);

        return new ImageEditResultDto { AssetId = newAssetId };
    }

    private async Task<ServiceResult<ImageEditResultDto>> HandleCopyWithPresetsAsync(
        Asset sourceAsset, ImageEditRequestDto dto, Stream renderedPng,
        long fileSize, CancellationToken ct)
    {
        if (dto.PresetIds is null || dto.PresetIds.Length == 0)
            return ServiceError.BadRequest("At least one preset ID is required for CopyWithPresets mode");

        var presets = await presetRepo.GetByIdsAsync(dto.PresetIds, ct);
        if (presets.Count != dto.PresetIds.Length)
            return ServiceError.BadRequest("One or more preset IDs are invalid");

        // First create the main copy
        var copyResult = await HandleCopyAsync(sourceAsset, dto, renderedPng, fileSize, ct);
        if (!copyResult.IsSuccess)
            return copyResult;

        var mainAssetId = copyResult.Value!.AssetId;

        // Queue preset generation via Wolverine
        await messageBus.PublishAsync(new ApplyExportPresetsCommand
        {
            SourceAssetId = mainAssetId,
            PresetIds = dto.PresetIds.ToList(),
            RequestedByUserId = currentUser.UserId!
        }, new DeliveryOptions { DeliverWithin = TimeSpan.FromMinutes(30) });

        logger.LogInformation(
            "Queued {PresetCount} preset(s) for asset {AssetId} from source {SourceId} by {UserId}",
            dto.PresetIds.Length, mainAssetId, sourceAsset.Id, currentUser.UserId);

        return new ImageEditResultDto
        {
            AssetId = mainAssetId,
            DerivativeAssetIds = new List<Guid>() // Will be populated async by the worker
        };
    }

}
