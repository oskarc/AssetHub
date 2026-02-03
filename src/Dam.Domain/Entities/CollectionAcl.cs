namespace Dam.Domain.Entities;

/// <summary>
/// Defines the available roles for collection access control.
/// Roles are hierarchical: higher roles include permissions of lower roles.
/// </summary>
public enum CollectionRole
{
    /// <summary>Can browse collections and view assets.</summary>
    Viewer = 1,
    /// <summary>Can upload and edit assets in assigned collections.</summary>
    Contributor = 2,
    /// <summary>Can manage collection settings and ACLs.</summary>
    Manager = 3,
    /// <summary>Full access including deletion.</summary>
    Admin = 4
}

/// <summary>
/// Access Control List entry for a collection.
/// Links a principal (user/group) to a collection with a specific role.
/// </summary>
public class CollectionAcl
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string PrincipalType { get; set; } = string.Empty; // "user" or "group"
    public string PrincipalId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // viewer|contributor|manager|admin
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Collection Collection { get; set; } = null!;

    /// <summary>
    /// Gets the role as an enum value.
    /// </summary>
    public CollectionRole? RoleEnum => Role?.ToLowerInvariant() switch
    {
        "viewer" => CollectionRole.Viewer,
        "contributor" => CollectionRole.Contributor,
        "manager" => CollectionRole.Manager,
        "admin" => CollectionRole.Admin,
        _ => null
    };

    /// <summary>
    /// Checks if this ACL entry has at least the specified role level.
    /// </summary>
    public bool HasAtLeastRole(CollectionRole requiredRole)
    {
        return RoleEnum.HasValue && RoleEnum.Value >= requiredRole;
    }
}
