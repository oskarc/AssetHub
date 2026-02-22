using AssetHub.Application;

namespace AssetHub.Ui.Services;

/// <summary>
/// UI-layer wrapper for role permissions.
/// Delegates to the shared RoleHierarchy in AssetHub.Application.
/// </summary>
public static class RolePermissions
{
    /// <summary>
    /// Checks if a user with the given role can upload assets (requires contributor+).
    /// </summary>
    public static bool CanUpload(string? role) => RoleHierarchy.CanUpload(role);

    /// <summary>
    /// Checks if a user with the given role can share assets/collections (requires contributor+).
    /// </summary>
    public static bool CanShare(string? role) => RoleHierarchy.CanShare(role);

    /// <summary>
    /// Checks if a user with the given role can edit asset metadata (requires contributor+).
    /// </summary>
    public static bool CanEdit(string? role) => RoleHierarchy.CanEdit(role);

    /// <summary>
    /// Checks if a user with the given role can manage collection membership (requires contributor+).
    /// </summary>
    public static bool CanManageCollections(string? role) => RoleHierarchy.CanManageCollections(role);

    /// <summary>
    /// Checks if a user with the given role can edit collection properties (requires manager+).
    /// </summary>
    public static bool CanEditCollection(string? role) => RoleHierarchy.CanEditCollection(role);

    /// <summary>
    /// Checks if a user with the given role can delete assets (requires manager+).
    /// </summary>
    public static bool CanDelete(string? role) => RoleHierarchy.CanDelete(role);

    /// <summary>
    /// Checks if a user with the given role can manage ACLs (requires manager+).
    /// </summary>
    public static bool CanManageAccess(string? role) => RoleHierarchy.CanManageAccess(role);

    /// <summary>
    /// Gets the numeric level for a role. Returns 0 for unknown roles.
    /// </summary>
    private static int GetRoleLevel(string? role) => RoleHierarchy.GetLevel(role);
}
