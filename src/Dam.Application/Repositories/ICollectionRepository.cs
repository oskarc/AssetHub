namespace Dam.Application.Repositories;

using Dam.Domain.Entities;

/// <summary>
/// Repository interface for Collection entities.
/// Handles database operations for collections with hierarchical relationships.
/// </summary>
public interface ICollectionRepository
{
    /// <summary>
    /// Gets a collection by ID with optional related data.
    /// </summary>
    Task<Collection?> GetByIdAsync(Guid id, bool includeAcls = false, bool includeChildren = false);

    /// <summary>
    /// Gets all root-level collections (no parent).
    /// </summary>
    Task<IEnumerable<Collection>> GetRootCollectionsAsync();

    /// <summary>
    /// Gets all child collections of a parent.
    /// </summary>
    Task<IEnumerable<Collection>> GetChildrenAsync(Guid parentId);

    /// <summary>
    /// Gets all collections accessible to a user (by checking ACLs).
    /// </summary>
    Task<IEnumerable<Collection>> GetAccessibleCollectionsAsync(string userId);

    /// <summary>
    /// Creates a new collection.
    /// </summary>
    Task<Collection> CreateAsync(Collection collection);

    /// <summary>
    /// Updates an existing collection.
    /// </summary>
    Task<Collection> UpdateAsync(Collection collection);

    /// <summary>
    /// Deletes a collection and its children.
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// Checks if a collection exists.
    /// </summary>
    Task<bool> ExistsAsync(Guid id);
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
    Task<IEnumerable<CollectionAcl>> GetByCollectionAsync(Guid collectionId);

    /// <summary>
    /// Gets ACL entry for a specific user/group on a collection.
    /// </summary>
    Task<CollectionAcl?> GetByPrincipalAsync(Guid collectionId, string principalType, string principalId);

    /// <summary>
    /// Creates or updates an ACL entry.
    /// </summary>
    Task<CollectionAcl> SetAccessAsync(Guid collectionId, string principalType, string principalId, string role);

    /// <summary>
    /// Removes access for a principal on a collection.
    /// </summary>
    Task RevokeAccessAsync(Guid collectionId, string principalType, string principalId);

    /// <summary>
    /// Removes all ACL entries for a collection (used when deleting).
    /// </summary>
    Task RevokeAllAccessAsync(Guid collectionId);
}
