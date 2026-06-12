namespace AssetHub.Domain.Entities;

/// <summary>
/// Role assigned to a principal on a collection (ACL).
/// Ordered from least to most privilege.
/// </summary>
public enum AclRole
{
    Viewer = 1,
    Contributor = 2,
    Manager = 3,
    Admin = 4
}

/// <summary>
/// Type of principal in an ACL entry.
/// </summary>
public enum PrincipalType
{
    User
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the ACL enums.
/// </summary>
// Each enum's ToDbString is kept next to its parser instead of grouping all
// ToDbString overloads together — easier to scan per-enum. Suppress S4136 for the file.
#pragma warning disable S4136
public static class AclEnumExtensions
{
    public static string ToDbString(this AclRole role) => role switch
    {
        AclRole.Viewer => "viewer",
        AclRole.Contributor => "contributor",
        AclRole.Manager => "manager",
        AclRole.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static AclRole ToAclRole(this string value) => value switch
    {
        "viewer" => AclRole.Viewer,
        "contributor" => AclRole.Contributor,
        "manager" => AclRole.Manager,
        "admin" => AclRole.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown ACL role: {value}")
    };

    public static string ToDbString(this PrincipalType type) => type switch
    {
        PrincipalType.User => "user",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static PrincipalType ToPrincipalType(this string value) => value switch
    {
        "user" => PrincipalType.User,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown principal type: {value}")
    };
}
#pragma warning restore S4136
