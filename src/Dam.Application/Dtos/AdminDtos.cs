using System.ComponentModel.DataAnnotations;

namespace Dam.Application.Dtos;

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
}

/// <summary>
/// Hierarchical collection with ACL information for admin view.
/// </summary>
public record CollectionAccessDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public Guid? ParentId { get; init; }
    public List<CollectionAclResponseDto> Acls { get; init; } = [];
    public List<CollectionAccessDto> Children { get; init; } = [];
}

/// <summary>
/// Request to set collection access (admin endpoint).
/// </summary>
public record SetCollectionAccessRequest
{
    [RegularExpression("^(user|group)$")]
    public string PrincipalType { get; init; } = "user";
    
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
