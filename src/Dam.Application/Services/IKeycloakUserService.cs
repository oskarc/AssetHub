namespace Dam.Application.Services;

/// <summary>
/// Service for managing users in Keycloak via the Admin REST API.
/// Used for operations that cannot be done via direct database queries
/// (e.g., creating users, resetting passwords).
/// </summary>
public interface IKeycloakUserService
{
    /// <summary>
    /// Creates a new user in Keycloak and returns the user's ID.
    /// </summary>
    /// <param name="username">The username (must be unique).</param>
    /// <param name="email">The user's email (must be unique).</param>
    /// <param name="firstName">The user's first name.</param>
    /// <param name="lastName">The user's last name.</param>
    /// <param name="password">The initial password.</param>
    /// <param name="temporaryPassword">If true, the user must change password on first login.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Keycloak user ID of the newly created user.</returns>
    /// <exception cref="KeycloakApiException">Thrown when user creation fails.</exception>
    Task<string> CreateUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        string password,
        bool temporaryPassword = true,
        CancellationToken ct = default);
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
