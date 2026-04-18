using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Orchestrates image edit operations: replace, copy, and copy-with-presets.
/// The rendered PNG is uploaded via presigned URL; this service manages the asset
/// records, lineage, and queuing of preset generation.
/// </summary>
public interface IImageEditingService
{
    /// <summary>
    /// Apply an image edit. Depending on the save mode, this will replace the original,
    /// create a linked copy, or create a copy and queue preset derivatives.
    /// </summary>
    Task<ServiceResult<ImageEditResultDto>> ApplyEditAsync(
        Guid sourceAssetId, ImageEditRequestDto dto, Stream renderedPng, string fileName,
        long fileSize, CancellationToken ct);
}
