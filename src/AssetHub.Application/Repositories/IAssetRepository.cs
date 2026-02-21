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
    /// <param name="allowedCollectionIds">If non-null, only return assets in these collections. Pass null for unfiltered (admin).</param>
    Task<(List<Asset> Assets, int Total)> SearchAllAsync(
        string? query = null,
        string? assetType = null,
        string sortBy = Constants.SortBy.CreatedDesc,
        int skip = 0,
        int take = 50,
        List<Guid>? allowedCollectionIds = null,
        bool includeAllStatuses = false,
        CancellationToken cancellationToken = default);
}
