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
    /// Gets a collection by exact name (case-insensitive).
    /// </summary>
    Task<Collection?> GetByNameAsync(string name, CancellationToken ct = default);

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

    /// <summary>
    /// Returns a Name lookup dictionary for the specified collection IDs.
    /// Missing IDs are simply absent from the result.
    /// </summary>
    Task<Dictionary<Guid, string>> GetNamesByIdsAsync(List<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Gets asset counts for a set of collection IDs.
    /// </summary>
    Task<Dictionary<Guid, int>> GetAssetCountsAsync(IEnumerable<Guid> collectionIds, CancellationToken ct = default);

    /// <summary>
    /// Gets the count of assets that would become orphaned (not in any other collection)
    /// if the specified collection were deleted.
    /// </summary>
    Task<int> GetOrphanedAssetCountAsync(Guid collectionId, CancellationToken ct = default);

    // ── Nested collections (T5-NEST-01) ──────────────────────────────────

    /// <summary>
    /// Returns just the <c>ParentCollectionId</c> for one collection. Used
    /// by the cycle-detection walk in <c>SetParentAsync</c>; cheaper than
    /// pulling the whole row.
    /// </summary>
    Task<Guid?> GetParentIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>(Id, ParentCollectionId, InheritParentAcl)</c> tuples for
    /// every collection in <paramref name="ids"/> plus every ancestor
    /// reachable up to <see cref="Constants.Limits.MaxCollectionDepth"/>
    /// hops. One round-trip via a recursive CTE; used by
    /// <c>ICollectionAuthorizationService.FilterAccessibleAsync</c> to walk
    /// the inheritance chain in memory.
    /// </summary>
    Task<Dictionary<Guid, (Guid? ParentId, bool InheritParentAcl)>> GetAncestorChainAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Returns the IDs of all collections that transitively inherit from
    /// <paramref name="rootId"/> through <c>InheritParentAcl = true</c>
    /// links. Used to cascade cache invalidation when an inheriting parent's
    /// ACL changes. Bounded by <see cref="Constants.Limits.MaxCollectionDepth"/>
    /// hops.
    /// </summary>
    Task<List<Guid>> GetInheritingDescendantIdsAsync(Guid rootId, CancellationToken ct = default);

    /// <summary>
    /// Returns the depth of the subtree rooted at <paramref name="id"/>. A leaf
    /// returns 1, a parent with one child returns 2, and so on. Walks at most
    /// <paramref name="cap"/> levels — if the actual depth exceeds the cap, the
    /// returned value will be <paramref name="cap"/> + 1, signalling "deeper
    /// than the budget allows" without enumerating the rest of the tree. Used
    /// by reparent validation so a moving collection's existing descendants are
    /// counted toward the depth limit.
    /// </summary>
    Task<int> GetMaxSubtreeDepthAsync(Guid id, int cap, CancellationToken ct = default);
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

    /// <summary>
    /// Deletes all ACL entries for a user across all collections. Returns the count deleted.
    /// </summary>
    Task<int> DeleteByUserAsync(string userId, CancellationToken ct = default);
}
