namespace Dam.Application.Services;

/// <summary>
/// Service for looking up user information from the identity provider.
/// This provides a way to resolve user IDs to usernames without calling external APIs directly.
/// </summary>
public interface IUserLookupService
{
    /// <summary>
    /// Gets a mapping of user IDs to usernames for the given user IDs.
    /// </summary>
    Task<Dictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the username for a single user ID.
    /// </summary>
    Task<string?> GetUserNameAsync(string userId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the user ID for a given username. Returns null if user doesn't exist.
    /// </summary>
    Task<string?> GetUserIdByUsernameAsync(string username, CancellationToken ct = default);
    
    /// <summary>
    /// Checks if a user with the given username exists.
    /// </summary>
    Task<bool> UserExistsAsync(string username, CancellationToken ct = default);
}
