using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface IAssetMetadataRepository
{
    /// <summary>Gets all metadata values for an asset.</summary>
    Task<List<AssetMetadataValue>> GetByAssetIdAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>Gets metadata values for multiple assets.</summary>
    Task<Dictionary<Guid, List<AssetMetadataValue>>> GetByAssetIdsAsync(IEnumerable<Guid> assetIds, CancellationToken ct = default);

    /// <summary>Replaces all metadata values for an asset. Atomic — delete and insert run in a single transaction.</summary>
    Task ReplaceForAssetAsync(Guid assetId, List<AssetMetadataValue> values, CancellationToken ct = default);

    /// <summary>
    /// Replaces metadata values for multiple assets in a single transaction.
    /// Either all assets are updated or none are — failure rolls back the whole batch.
    /// </summary>
    Task ReplaceForAssetsAsync(IEnumerable<(Guid AssetId, List<AssetMetadataValue> Values)> batch, CancellationToken ct = default);

    /// <summary>Deletes all metadata values for an asset.</summary>
    Task DeleteByAssetIdAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>Deletes all metadata values referencing fields in a schema.</summary>
    Task DeleteBySchemaIdAsync(Guid schemaId, CancellationToken ct = default);
}
