namespace AssetHub.Application.Services;

/// <summary>
/// Removes a user's application data (ACLs and active shares).
/// Shared between AdminEndpoints.DeleteUser and UserSyncService.
/// </summary>
public interface IUserCleanupService
{
    /// <summary>
    /// Removes all collection ACLs and revokes all active shares for a given user.
    /// </summary>
    /// <returns>The count of ACLs removed and shares revoked.</returns>
    Task<(int AclsRemoved, int SharesRevoked)> CleanupUserDataAsync(
        string userId, CancellationToken ct = default);
}
