namespace AssetHub.Application.Repositories;

using AssetHub.Domain.Entities;

/// <summary>
/// Repository interface for Collection entities.
/// Handles database operations for collections.
/// </summary>
public interface ICollectionRepository
{
    /// <summary>
    /// Gets a collection by ID with optional related data.
    /// </summary>
    Task<Collection?> GetByIdAsync(Guid id, bool includeAcls = false, CancellationToken ct = default);

    /// <summary>
    /// Gets all collections.
    /// </summary>
    Task<IEnumerable<Collection>> GetRootCollectionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all collections accessible to a user (by checking ACLs).
    /// </summary>
    Task<IEnumerable<Collection>> GetAccessibleCollectionsAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new collection.
    /// </summary>
    Task<Collection> CreateAsync(Collection collection, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing collection.
    /// </summary>
    Task<Collection> UpdateAsync(Collection collection, CancellationToken ct = default);

    /// <summary>
    /// Deletes a collection.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Checks if a collection exists.
    /// </summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Checks if a collection with the given name already exists (case-insensitive).
    /// </summary>
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>
    /// Gets collection names for a set of asset IDs (via the AssetCollections join table).
    /// </summary>
    Task<Dictionary<Guid, List<string>>> GetCollectionNamesForAssetsAsync(List<Guid> assetIds, CancellationToken ct = default);

    /// <summary>
    /// Gets all collections with their ACLs (admin use).
    /// </summary>
    Task<IEnumerable<Collection>> GetAllWithAclsAsync(CancellationToken ct = default);
}

/// <summary>
/// Repository interface for Collection ACL entries.
/// Handles role-based access control management.
/// </summary>
public interface ICollectionAclRepository
{
    /// <summary>
    /// Gets ACL entries for a collection.
    /// </summary>
    Task<IEnumerable<CollectionAcl>> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Gets ACL entry for a specific user/group on a collection.
    /// </summary>
    Task<CollectionAcl?> GetByPrincipalAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets all ACL entries for a specific user across all collections.
    /// </summary>
    Task<IEnumerable<CollectionAcl>> GetByUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates an ACL entry.
    /// </summary>
    Task<CollectionAcl> SetAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default);

    /// <summary>
    /// Removes access for a principal on a collection.
    /// </summary>
    Task RevokeAccessAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default);

    /// <summary>
    /// Removes all ACL entries for a collection (used when deleting).
    /// </summary>
    Task RevokeAllAccessAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Gets all ACL entries across all collections (admin use).
    /// </summary>
    Task<IEnumerable<CollectionAcl>> GetAllAsync(CancellationToken ct = default);
}
