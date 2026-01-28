using Dam.Domain.Entities;

namespace Dam.Application.Repositories;

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
    Task DeleteByCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search assets within a collection with optional filters.
    /// </summary>
    Task<(List<Asset> Assets, int Total)> SearchAsync(
        Guid collectionId,
        string? query = null,
        string? assetType = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
}
