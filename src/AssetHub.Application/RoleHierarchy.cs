namespace AssetHub.Application;

/// <summary>
/// Centralized role hierarchy definitions used across all layers.
/// Defines the role-based access control levels for the DAM system.
/// </summary>
public static class RoleHierarchy
{
    /// <summary>
    /// Role name constants.
    /// </summary>
    public static class Roles
    {
        public const string Viewer = "viewer";
        public const string Contributor = "contributor";
        public const string Manager = "manager";
        public const string Admin = "admin";
    }

    /// <summary>
    /// Role levels for hierarchy comparison.
    /// Higher values indicate more permissions.
    /// </summary>
    private static readonly Dictionary<string, int> Levels = new(StringComparer.OrdinalIgnoreCase)
    {
        { Roles.Viewer, 1 },
        { Roles.Contributor, 2 },
        { Roles.Manager, 3 },
        { Roles.Admin, 4 }
    };

    /// <summary>
    /// Gets the numeric level for a role. Returns 0 for unknown roles.
    /// </summary>
    public static int GetLevel(string? role)
    {
        if (string.IsNullOrEmpty(role)) return 0;
        return Levels.TryGetValue(role, out var level) ? level : 0;
    }

    /// <summary>
    /// Checks if the user role meets or exceeds the required role level.
    /// </summary>
    public static bool MeetsRequirement(string? userRole, string requiredRole)
    {
        return GetLevel(userRole) >= GetLevel(requiredRole);
    }

    /// <summary>
    /// Checks if user can upload assets (requires contributor+).
    /// </summary>
    public static bool CanUpload(string? role) => GetLevel(role) >= 2;

    /// <summary>
    /// Checks if user can share assets/collections (requires contributor+).
    /// </summary>
    public static bool CanShare(string? role) => GetLevel(role) >= 2;

    /// <summary>
    /// Checks if user can edit asset metadata (requires contributor+).
    /// </summary>
    public static bool CanEdit(string? role) => GetLevel(role) >= 2;

    /// <summary>
    /// Checks if user can manage collection membership (requires contributor+).
    /// </summary>
    public static bool CanManageCollections(string? role) => GetLevel(role) >= 2;

    /// <summary>
    /// Checks if user can edit collection properties like name and description (requires manager+).
    /// </summary>
    public static bool CanEditCollection(string? role) => GetLevel(role) >= 3;

    /// <summary>
    /// Checks if user can delete assets (requires manager+).
    /// </summary>
    public static bool CanDelete(string? role) => GetLevel(role) >= 3;

    /// <summary>
    /// Checks if user can manage ACLs (requires manager+).
    /// </summary>
    public static bool CanManageAccess(string? role) => GetLevel(role) >= 3;

    /// <summary>
    /// Generic role-level guard: returns true when the caller's role level is
    /// at least as high as the target role level. Use this for any operation
    /// that requires "you can only affect roles at or below your own".
    /// </summary>
    public static bool HasSufficientLevel(string? callerRole, string? targetRole)
    {
        return GetLevel(callerRole) >= GetLevel(targetRole);
    }

    /// <summary>
    /// Checks if a caller can grant/set a target role.
    /// Convenience wrapper around <see cref="HasSufficientLevel"/>.
    /// </summary>
    public static bool CanGrantRole(string? callerRole, string targetRole)
        => HasSufficientLevel(callerRole, targetRole);

    /// <summary>
    /// Checks if a caller can revoke access from a target with the given role.
    /// Convenience wrapper around <see cref="HasSufficientLevel"/>.
    /// </summary>
    public static bool CanRevokeRole(string? callerRole, string? targetRole)
        => HasSufficientLevel(callerRole, targetRole);

    /// <summary>
    /// All valid role names.
    /// </summary>
    public static IReadOnlyCollection<string> AllRoles => Levels.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Returns the highest role from a set of roles based on the hierarchy.
    /// Falls back to Viewer if the set is empty or contains only unknown roles.
    /// </summary>
    public static string GetHighestRole(IEnumerable<string> roles)
    {
        return roles
            .OrderByDescending(r => GetLevel(r))
            .FirstOrDefault(r => GetLevel(r) > 0) ?? Roles.Viewer;
    }

    /// <summary>
    /// Resolves a role string to a valid role, falling back to Viewer for unknown values.
    /// </summary>
    public static string ResolveRole(string? role)
    {
        var normalized = role?.ToLowerInvariant() ?? "";
        return AllRoles.Contains(normalized) ? normalized : Roles.Viewer;
    }
}
