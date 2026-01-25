namespace Dam.Application.Dtos;

/// <summary>
/// DTO for creating a new collection.
/// </summary>
public class CreateCollectionDto
{
    /// <summary>
    /// Collection name (required, 1-255 chars).
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Collection description (optional).
    /// </summary>
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
    public string? Name { get; set; }

    /// <summary>
    /// Collection description.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// DTO for collection responses (GET endpoints).
/// </summary>
public class CollectionResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string UserRole { get; set; } = ""; // viewer, contributor, manager, admin
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public int ChildCount { get; set; }
    public int AssetCount { get; set; }
}

/// <summary>
/// DTO for ACL assignment requests.
/// </summary>
public class SetCollectionAccessDto
{
    /// <summary>
    /// Principal type: "user" or "group".
    /// </summary>
    public required string PrincipalType { get; set; }

    /// <summary>
    /// Principal ID (user ID or group ID from Keycloak).
    /// </summary>
    public required string PrincipalId { get; set; }

    /// <summary>
    /// Role to assign: "viewer", "contributor", "manager", "admin".
    /// </summary>
    public required string Role { get; set; }
}

/// <summary>
/// DTO for ACL responses.
/// </summary>
public class CollectionAclResponseDto
{
    public Guid Id { get; set; }
    public string PrincipalType { get; set; } = "";
    public string PrincipalId { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
