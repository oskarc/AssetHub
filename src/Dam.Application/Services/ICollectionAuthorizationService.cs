namespace Dam.Application.Services;

/// <summary>
/// Defines authorization operations for the DAM system.
/// Handles role-based access control at the collection level.
/// </summary>
public interface ICollectionAuthorizationService
{
    /// <summary>
    /// Checks if a user has a specific role on a collection.
    /// </summary>
    /// <param name="userId">User ID (from Keycloak token)</param>
    /// <param name="collectionId">Collection ID to check</param>
    /// <param name="requiredRole">Required role: "viewer", "contributor", "manager", "admin"</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if user has the role or higher</returns>
    Task<bool> CheckAccessAsync(string userId, Guid collectionId, string requiredRole, CancellationToken ct = default);

    /// <summary>
    /// Gets the user's role on a specific collection.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Role name or null if no access</returns>
    Task<string?> GetUserRoleAsync(string userId, Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Checks if user can manage ACL (must be owner/admin or collection manager)
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task<bool> CanManageAclAsync(string userId, Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a user's effective role on a collection is inherited from a parent
    /// (i.e., no direct ACL exists on the collection itself).
    /// </summary>
    Task<bool> IsRoleInheritedAsync(string userId, Guid collectionId, CancellationToken ct = default);

    /// <summary>
    /// Checks if user can create collections at root level.
    /// Currently: all authenticated users can create root collections.
    /// </summary>
    Task<bool> CanCreateRootCollectionAsync(string userId);

    /// <summary>
    /// Checks if user can create a sub-collection under a parent.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    Task<bool> CanCreateSubCollectionAsync(string userId, Guid parentCollectionId, CancellationToken ct = default);
}
