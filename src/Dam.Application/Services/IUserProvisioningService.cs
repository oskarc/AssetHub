using Dam.Application.Repositories;

namespace Dam.Application.Services;

/// <summary>
/// Service interface for user provisioning orchestration.
/// Handles validation and setup steps when creating new users.
/// </summary>
public interface IUserProvisioningService
{
    /// <summary>
    /// Validates that all specified collection IDs exist in the database.
    /// Returns a dictionary of errors keyed by "collection_{id}" (empty if all valid).
    /// </summary>
    Task<Dictionary<string, string>> ValidateCollectionsExistAsync(
        List<Guid> collectionIds, CancellationToken ct = default);

    /// <summary>
    /// Grants collection access to a user for each specified collection.
    /// Logs but does not throw on per-collection failures.
    /// </summary>
    Task GrantCollectionAccessAsync(
        List<Guid> collectionIds, string userId, string role, string username,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a welcome email to a newly created user with login credentials.
    /// Logs but does not throw on failure.
    /// </summary>
    Task SendWelcomeEmailAsync(
        string email, string username, string password, bool requirePasswordChange,
        string baseUrl, string adminUsername, CancellationToken ct = default);
}
