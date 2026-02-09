using System.ComponentModel.DataAnnotations;

namespace Dam.Application.Dtos;

/// <summary>
/// DTO for creating a new collection.
/// </summary>
public class CreateCollectionDto
{
    /// <summary>
    /// Collection name (required, 1-255 chars).
    /// </summary>
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Name { get; set; }

    /// <summary>
    /// Collection description (optional, max 2000 chars).
    /// </summary>
    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Parent collection ID. Null = root level collection.
    /// </summary>
    public Guid? ParentId { get; set; }
}

/// <summary>
/// DTO for updating a collection.
/// </summary>
public class UpdateCollectionDto
{
    /// <summary>
    /// Collection name.
    /// </summary>
    [StringLength(255, MinimumLength = 1)]
    public string? Name { get; set; }

    /// <summary>
    /// Collection description.
    /// </summary>
    [StringLength(2000)]
    public string? Description { get; set; }
}

/// <summary>
/// DTO for collection responses (GET endpoints).
/// </summary>
public record CollectionResponseDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public Guid? ParentId { get; init; }
    public string UserRole { get; init; } = "";
    /// <summary>
    /// True when the user's effective role is inherited from a parent collection
    /// rather than being directly assigned on this collection.
    /// </summary>
    public bool IsRoleInherited { get; init; }
    public DateTime CreatedAt { get; init; }
    public string CreatedByUserId { get; init; } = "";
    public int ChildCount { get; init; }
    public int AssetCount { get; init; }
}

/// <summary>
/// DTO for ACL assignment requests.
/// </summary>
public class SetCollectionAccessDto
{
    /// <summary>
    /// Principal type: "user".
    /// </summary>
    [Required]
    [RegularExpression("^(user)$", ErrorMessage = "PrincipalType must be 'user'")]
    public required string PrincipalType { get; set; }

    /// <summary>
    /// Principal ID (user ID from Keycloak).
    /// </summary>
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string PrincipalId { get; set; }

    /// <summary>
    /// Role to assign: "viewer", "contributor", "manager", "admin".
    /// </summary>
    [Required]
    [RegularExpression("^(viewer|contributor|manager|admin)$", ErrorMessage = "Role must be 'viewer', 'contributor', 'manager', or 'admin'")]
    public required string Role { get; set; }
}

/// <summary>
/// DTO for ACL responses.
/// </summary>
public record CollectionAclResponseDto
{
    public Guid Id { get; init; }
    public string PrincipalType { get; init; } = "";
    public string PrincipalId { get; init; } = "";
    public string? PrincipalName { get; init; }
    public string Role { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Lightweight DTO for user search results (used in ACL management).
/// </summary>
public record UserSearchResultDto
{
    public string Id { get; init; } = "";
    public string Username { get; init; } = "";
    public string? Email { get; init; }
}
