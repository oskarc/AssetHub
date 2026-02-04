using Dam.Domain.Entities;

namespace Dam.Application.Repositories;

/// <summary>
/// Repository interface for managing asset-collection relationships.
/// Allows assets to belong to multiple collections.
/// </summary>
public interface IAssetCollectionRepository
{
    /// <summary>
    /// Gets all collections an asset belongs to (including primary collection).
    /// </summary>
    Task<List<Collection>> GetCollectionsForAssetAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>
    /// Gets all asset-collection relationships for an asset.
    /// </summary>
    Task<List<AssetCollection>> GetByAssetAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>
    /// Gets all asset-collection relationships for a collection.
    /// </summary>
    Task<List<AssetCollection>> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Adds an asset to a collection. Returns null if already exists.
    /// </summary>
    Task<AssetCollection?> AddToCollectionAsync(Guid assetId, Guid collectionId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Removes an asset from a collection.
    /// </summary>
    Task<bool> RemoveFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Checks if an asset belongs to a collection (either primary or via join table).
    /// </summary>
    Task<bool> BelongsToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the IDs of all collections an asset is linked to (excluding primary collection).
    /// </summary>
    Task<List<Guid>> GetCollectionIdsForAssetAsync(Guid assetId, CancellationToken ct = default);
}
