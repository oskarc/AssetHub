namespace AssetHub.Application.Services;

/// <summary>
/// Administrative operations against the configured identity provider — user
/// lifecycle and role membership.
///
/// Implemented twice: <c>KeycloakUserDirectoryAdmin</c> calls the Keycloak Admin
/// REST API, <c>IdentityUserDirectoryAdmin</c> uses the local Identity stores.
/// Selected by <c>Auth:Provider</c>, so callers stay provider-agnostic.
/// </summary>
public interface IUserDirectoryAdmin
{
    /// <summary>
    /// Creates a new user and returns the user's ID.
    /// </summary>
    /// <param name="username">The username (must be unique).</param>
    /// <param name="email">The user's email (must be unique).</param>
    /// <param name="firstName">The user's first name.</param>
    /// <param name="lastName">The user's last name.</param>
    /// <param name="password">The initial password.</param>
    /// <param name="temporaryPassword">If true, the user must change password on first login.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ID of the newly created user.</returns>
    Task<string> CreateUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        string password,
        bool temporaryPassword = true,
        CancellationToken ct = default);


    /// <summary>
    /// Sends the user a link to complete the specified required actions
    /// (currently only UPDATE_PASSWORD).
    ///
    /// Under Keycloak this delegates to the execute-actions-email Admin API.
    /// Under Identity it mints a password-reset token and sends the app's own
    /// reset email — the action list is honoured, the mechanism differs.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="actions">The required actions (e.g., "UPDATE_PASSWORD").</param>
    /// <param name="lifespan">Optional link lifespan in seconds (default: Keycloak server default).</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendExecuteActionsEmailAsync(
        string userId,
        IEnumerable<string> actions,
        int? lifespan = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the IDs of all users who have a specific realm role (e.g. "admin").
    /// </summary>
    Task<HashSet<string>> GetRealmRoleMemberIdsAsync(string roleName, CancellationToken ct = default);

    /// <summary>
    /// Assigns a realm role to a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="roleName">The realm role name (e.g., "admin").</param>
    /// <param name="ct">Cancellation token.</param>
    Task AssignRealmRoleAsync(string userId, string roleName, CancellationToken ct = default);

    /// <summary>
    /// Removes a realm role from a user. No-op when the user does not have
    /// the role — Keycloak's DELETE /role-mappings endpoint accepts that.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="roleName">The realm role name (e.g., "admin").</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveRealmRoleAsync(string userId, string roleName, CancellationToken ct = default);
}

/// <summary>
/// Exception thrown when a Keycloak Admin API call fails.
/// </summary>
public class KeycloakApiException : Exception
{
    public int StatusCode { get; }
    
    public KeycloakApiException(string message, int statusCode = 0) : base(message)
    {
        StatusCode = statusCode;
    }
    
    public KeycloakApiException(string message, int statusCode, Exception innerException) 
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
