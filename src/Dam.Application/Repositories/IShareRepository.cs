using Dam.Domain.Entities;

namespace Dam.Application.Repositories;

public interface IShareRepository
{
    Task<Share?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Share?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<List<Share>> GetByScopeAsync(string scopeType, Guid scopeId, CancellationToken cancellationToken = default);
    Task<List<Share>> GetByUserAsync(string userId, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all shares with optional navigation properties (admin use).
    /// </summary>
    Task<List<Share>> GetAllAsync(bool includeAsset = false, bool includeCollection = false, CancellationToken cancellationToken = default);
    
    Task CreateAsync(Share share, CancellationToken cancellationToken = default);
    Task UpdateAsync(Share share, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments the access count and updates LastAccessedAt using a single SQL UPDATE.
    /// Avoids the read-modify-write race condition of fetching the entity first.
    /// </summary>
    Task IncrementAccessAsync(Guid id, CancellationToken cancellationToken = default);
}
