using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface IAssetRepository
{
    Task<Asset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Asset>> GetByCollectionAsync(Guid collectionId, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task<List<Asset>> GetByTypeAsync(string assetType, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task<List<Asset>> GetByStatusAsync(string status, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task<List<Asset>> GetByUserAsync(string userId, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task<int> CountByCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);
    Task<int> CountByStatusAsync(string status, CancellationToken cancellationToken = default);
    Task<Asset?> GetByOriginalKeyAsync(string objectKey, CancellationToken cancellationToken = default);
    Task CreateAsync(Asset asset, CancellationToken cancellationToken = default);
    Task UpdateAsync(Asset asset, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Smart collection-scoped deletion. Assets exclusive to this collection are hard-deleted
    /// and returned so the caller can clean up storage. Shared assets are only unlinked.
    /// </summary>
    Task<List<Asset>> DeleteByCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search assets within a collection with optional filters.
    /// </summary>
    Task<(List<Asset> Assets, int Total)> SearchAsync(
        Guid collectionId,
        string? query = null,
        string? assetType = null,
        string sortBy = Constants.SortBy.CreatedDesc,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search all assets with optional filters, restricted to specific collections.
    /// </summary>
    Task<(List<Asset> Assets, int Total)> SearchAllAsync(
        AssetSearchFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a Title lookup dictionary for the specified asset IDs.
    /// Missing IDs are simply absent from the result.
    /// </summary>
    Task<Dictionary<Guid, string>> GetTitlesByIdsAsync(List<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of derivative assets whose SourceAssetId matches the given ID.
    /// </summary>
    Task<int> CountDerivativesAsync(Guid sourceAssetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns derivative assets whose SourceAssetId matches the given ID.
    /// </summary>
    Task<List<Asset>> GetDerivativesAsync(Guid sourceAssetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find an existing asset by SHA256 hash (for duplicate detection during migration).
    /// </summary>
    Task<Asset?> GetBySha256Async(string sha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an asset including soft-deleted rows (IgnoreQueryFilters). For trash operations
    /// where a deleted asset must be restored or purged.
    /// </summary>
    Task<Asset?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated list of soft-deleted assets ordered by DeletedAt descending. Newest in trash first.
    /// </summary>
    Task<(List<Asset> Assets, int Total)> GetTrashAsync(
        int skip = 0, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns soft-deleted assets whose DeletedAt is older than the given cutoff.
    /// Bounded by `batchSize` so the purge worker doesn't pull millions of rows in one shot.
    /// </summary>
    Task<List<Asset>> GetTrashOlderThanAsync(
        DateTime cutoff, int batchSize = 100, CancellationToken cancellationToken = default);
}
