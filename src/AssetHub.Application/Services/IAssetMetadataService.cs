using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Operations for managing structured metadata values on assets.
/// </summary>
public interface IAssetMetadataService
{
    /// <summary>Gets all metadata values for an asset.</summary>
    Task<ServiceResult<List<AssetMetadataValueDto>>> GetByAssetIdAsync(Guid assetId, CancellationToken ct);

    /// <summary>Sets (replaces) metadata values for an asset.</summary>
    Task<ServiceResult<List<AssetMetadataValueDto>>> SetAsync(Guid assetId, SetAssetMetadataDto dto, CancellationToken ct);

    /// <summary>Sets metadata values for multiple assets in bulk.</summary>
    Task<ServiceResult> BulkSetAsync(BulkSetAssetMetadataDto dto, CancellationToken ct);
}
