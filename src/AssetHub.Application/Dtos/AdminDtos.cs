using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// Paginated response for the admin shares list endpoint.
/// </summary>
public record AdminSharesResponse
{
    public int Total { get; init; }
    public List<AdminShareDto> Items { get; init; } = [];
}

/// <summary>
/// Admin view of a share with statistics.
/// </summary>
public record AdminShareDto
{
    public Guid Id { get; init; }
    public required string ScopeType { get; init; }
    public Guid ScopeId { get; init; }
    public required string ScopeName { get; init; }
    public required string CreatedByUserId { get; init; }
    public required string CreatedByUserName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public DateTime? LastAccessedAt { get; init; }
    public int AccessCount { get; init; }
    public bool HasPassword { get; init; }
    public required string Status { get; init; }
    public List<string> CollectionNames { get; init; } = new();
}

/// <summary>
/// Collection with ACL information for admin view.
/// </summary>
public record CollectionAccessDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public List<CollectionAclResponseDto> Acls { get; init; } = [];
}

/// <summary>
/// Request to set collection access (admin endpoint).
/// </summary>
public record SetCollectionAccessRequest
{
    [RegularExpression("^(user|group)$")]
    public string PrincipalType { get; init; } = Constants.PrincipalTypes.User;
    
    [Required]
    public string? PrincipalId { get; init; }
    
    [Required]
    [RegularExpression("^(viewer|contributor|manager|admin)$")]
    public string? Role { get; init; }
}

/// <summary>
/// Summary of a user's access across all collections.
/// </summary>
public record UserAccessSummaryDto
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public int CollectionCount { get; init; }
    public required string HighestRole { get; init; }
    public List<UserCollectionAccessDto> Collections { get; init; } = [];
}

/// <summary>
/// A user's access to a specific collection.
/// </summary>
public record UserCollectionAccessDto
{
    public Guid CollectionId { get; init; }
    public required string CollectionName { get; init; }
    public required string Role { get; init; }
}

/// <summary>
/// User details from Keycloak.
/// </summary>
public record KeycloakUserDto
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateTime? CreatedAt { get; init; }
    public int CollectionCount { get; init; }
    public string? HighestRole { get; init; }
    /// <summary>True when the user has the global "admin" Keycloak realm role.</summary>
    public bool IsSystemAdmin { get; init; }
}

/// <summary>
/// Request to create a new user via Keycloak Admin API.
/// </summary>
public record CreateUserRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_\-\.]+$", ErrorMessage = "Username can only contain letters, numbers, underscores, hyphens, and dots")]
    public string Username { get; init; } = "";

    [Required]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; init; } = "";

    [Required]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "First name is required")]
    public string FirstName { get; init; } = "";

    [Required]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Last name is required")]
    public string LastName { get; init; } = "";

    /// <summary>
    /// Optional password. If omitted, one is generated server-side.
    /// Admin never sees the password — the user sets their own via email.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// If true, the user must change their password on first login.
    /// </summary>
    public bool RequirePasswordChange { get; init; } = true;

    /// <summary>
    /// If true, a welcome email with login credentials will be sent to the user.
    /// </summary>
    public bool SendWelcomeEmail { get; init; } = true;

    /// <summary>
    /// Optional: Collection IDs to grant initial access to.
    /// </summary>
    public List<Guid> InitialCollectionIds { get; init; } = [];

    /// <summary>
    /// Role to assign for initial collections (default: viewer).
    /// </summary>
    [RegularExpression("^(viewer|contributor|manager|admin)$", ErrorMessage = "Invalid role")]
    public string InitialRole { get; init; } = "viewer";

    /// <summary>
    /// If true, the user will be assigned the global "admin" Keycloak realm role.
    /// When enabled, collection-level access is not needed as admins have full access.
    /// </summary>
    public bool IsSystemAdmin { get; init; }
}

/// <summary>
/// Response after successfully creating a user.
/// </summary>
public record CreateUserResponse
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string Message { get; init; }
}

// ResetPasswordRequest removed — admin-initiated password resets are now
// handled by sending a Keycloak "execute-actions-email" (UPDATE_PASSWORD)
// so the admin never sees or types a user's password.

/// <summary>
/// Response containing a decrypted share token (admin only).
/// </summary>
public record ShareTokenResponse
{
    public required string Token { get; init; }
}

/// <summary>
/// Response containing a decrypted share password (admin only).
/// </summary>
public record SharePasswordResponse
{
    public required string Password { get; init; }
}

/// <summary>
/// Response from deleting a user.
/// </summary>
public record DeleteUserResponse
{
    public required string Message { get; init; }
    public int AclsRemoved { get; init; }
    public int SharesRevoked { get; init; }
}

/// <summary>
/// Request to delete multiple collections at once (admin only).
/// </summary>
public record BulkDeleteCollectionsRequest
{
    [Required]
    public List<Guid> CollectionIds { get; init; } = [];

    /// <summary>
    /// When true, assets exclusive to the deleted collections are permanently removed.
    /// When false, assets are unlinked but kept in the system.
    /// </summary>
    public bool DeleteAssets { get; init; } = true;
}

/// <summary>
/// Response from bulk-deleting collections.
/// </summary>
public record BulkDeleteCollectionsResponse
{
    public required string Message { get; init; }
    public int Deleted { get; init; }
    public int Failed { get; init; }
    public List<BulkOperationError> Errors { get; init; } = [];
}

/// <summary>
/// Request to set access on multiple collections at once (admin only).
/// </summary>
public record BulkSetCollectionAccessRequest
{
    [Required]
    public List<Guid> CollectionIds { get; init; } = [];

    [Required]
    public string? PrincipalId { get; init; }

    [RegularExpression("^(user)$")]
    public string PrincipalType { get; init; } = Constants.PrincipalTypes.User;

    [Required]
    [RegularExpression("^(viewer|contributor|manager|admin)$")]
    public string? Role { get; init; }
}

/// <summary>
/// Response from bulk setting collection access.
/// </summary>
public record BulkSetCollectionAccessResponse
{
    public required string Message { get; init; }
    public int Updated { get; init; }
    public int Failed { get; init; }
    public List<BulkOperationError> Errors { get; init; } = [];
}

/// <summary>
/// Describes a single failure in a bulk operation.
/// </summary>
public record BulkOperationError
{
    public Guid CollectionId { get; init; }
    public required string Error { get; init; }
}

/// <summary>
/// Request to delete multiple assets at once from a collection.
/// </summary>
public record BulkDeleteAssetsRequest
{
    [Required]
    public List<Guid> AssetIds { get; init; } = [];

    /// <summary>
    /// When set, assets are removed from this collection (and auto-deleted if orphaned).
    /// When null, a full permanent delete is performed.
    /// </summary>
    public Guid? FromCollectionId { get; init; }
}

/// <summary>
/// Response from bulk-deleting assets.
/// </summary>
public record BulkDeleteAssetsResponse
{
    public required string Message { get; init; }
    public int Deleted { get; init; }
    public int Failed { get; init; }
    public List<BulkAssetError> Errors { get; init; } = [];
}

/// <summary>
/// Describes a single asset failure in a bulk operation.
/// </summary>
public record BulkAssetError
{
    public Guid AssetId { get; init; }
    public required string Error { get; init; }
}
