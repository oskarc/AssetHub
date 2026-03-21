using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public sealed record ShareQueryOptions(
    bool IncludeAsset = false,
    bool IncludeCollection = false,
    int Skip = 0,
    int Take = Constants.Limits.DefaultAdminPageSize);

public interface IShareRepository
{
    Task<Share?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Share?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<List<Share>> GetByScopeAsync(string scopeType, Guid scopeId, CancellationToken cancellationToken = default);
    Task<List<Share>> GetByUserAsync(string userId, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all shares with optional navigation properties (admin use).
    /// </summary>
    Task<List<Share>> GetAllAsync(ShareQueryOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of shares (admin use).
    /// </summary>
    Task<int> CountAllAsync(CancellationToken cancellationToken = default);
    
    Task CreateAsync(Share share, CancellationToken cancellationToken = default);
    Task UpdateAsync(Share share, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all shares for a given scope (e.g. all shares for an asset or collection).
    /// </summary>
    Task DeleteByScopeAsync(string scopeType, Guid scopeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments the access count and updates LastAccessedAt using a single SQL UPDATE.
    /// Avoids the read-modify-write race condition of fetching the entity first.
    /// </summary>
    Task IncrementAccessAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes shares that reference non-existent assets or collections (orphaned shares).
    /// Returns the number of shares deleted.
    /// </summary>
    Task<int> DeleteOrphanedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all expired shares (not revoked, past expiry date).
    /// Returns the number of shares deleted.
    /// </summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all revoked shares.
    /// Returns the number of shares deleted.
    /// </summary>
    Task<int> DeleteRevokedAsync(CancellationToken cancellationToken = default);
}
