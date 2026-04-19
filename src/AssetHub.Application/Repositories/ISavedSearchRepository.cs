using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface ISavedSearchRepository
{
    /// <summary>Lists saved searches for an owner, newest first.</summary>
    Task<List<SavedSearch>> GetByOwnerAsync(string ownerUserId, CancellationToken ct = default);

    /// <summary>Gets a saved search by id, scoped to its owner.</summary>
    Task<SavedSearch?> GetByIdAsync(Guid id, string ownerUserId, CancellationToken ct = default);

    /// <summary>True when the owner already has a saved search with this name (exclude supports updates).</summary>
    Task<bool> ExistsByNameAsync(string ownerUserId, string name, Guid? excludeId = null, CancellationToken ct = default);

    Task<SavedSearch> CreateAsync(SavedSearch savedSearch, CancellationToken ct = default);

    Task<SavedSearch> UpdateAsync(SavedSearch savedSearch, CancellationToken ct = default);

    /// <summary>Deletes a saved search by id, scoped to its owner. No-op if it doesn't belong to the caller.</summary>
    Task DeleteAsync(Guid id, string ownerUserId, CancellationToken ct = default);
}
