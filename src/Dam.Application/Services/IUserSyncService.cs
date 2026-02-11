namespace Dam.Application.Services;

/// <summary>
/// Service for synchronizing application data with the identity provider.
/// Detects and cleans up references to users that no longer exist in Keycloak.
/// </summary>
public interface IUserSyncService
{
    /// <summary>
    /// Scans ACLs and shares for user IDs that no longer exist in Keycloak,
    /// removes orphaned ACLs, revokes orphaned shares, and returns a summary.
    /// </summary>
    Task<UserSyncResult> SyncDeletedUsersAsync(bool dryRun = false, CancellationToken ct = default);
}

/// <summary>
/// Result of a user sync operation.
/// </summary>
public class UserSyncResult
{
    /// <summary>Total distinct user IDs referenced in app data.</summary>
    public int TotalReferencedUsers { get; set; }
    
    /// <summary>User IDs that still exist in Keycloak.</summary>
    public int ActiveUsers { get; set; }
    
    /// <summary>User IDs that no longer exist in Keycloak.</summary>
    public int DeletedUsers { get; set; }
    
    /// <summary>ACL entries removed (or that would be removed in dry run).</summary>
    public int AclsRemoved { get; set; }
    
    /// <summary>Shares revoked (or that would be revoked in dry run).</summary>
    public int SharesRevoked { get; set; }
    
    /// <summary>Whether this was a dry run (no changes made).</summary>
    public bool DryRun { get; set; }
    
    /// <summary>Details of deleted users found.</summary>
    public List<DeletedUserInfo> DeletedUserDetails { get; set; } = new();
}

/// <summary>
/// Info about a deleted user and their orphaned data.
/// </summary>
public class DeletedUserInfo
{
    public string UserId { get; set; } = string.Empty;
    public int AclCount { get; set; }
    public int ShareCount { get; set; }
}
