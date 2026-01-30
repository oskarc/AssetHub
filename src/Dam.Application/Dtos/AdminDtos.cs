namespace Dam.Application.Dtos;

/// <summary>
/// DTOs for admin endpoints and UI.
/// </summary>

public record AdminShareDto
{
    public Guid Id { get; init; }
    public string ScopeType { get; init; } = string.Empty;
    public Guid ScopeId { get; init; }
    public string ScopeName { get; init; } = string.Empty;
    public string CreatedByUserId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public DateTime? LastAccessedAt { get; init; }
    public int AccessCount { get; init; }
    public bool HasPassword { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record CollectionAccessDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ParentId { get; init; }
    public List<CollectionAclDto> Acls { get; init; } = new();
    public List<CollectionAccessDto> Children { get; init; } = new();
}

public record CollectionAclDto
{
    public Guid Id { get; init; }
    public string PrincipalType { get; init; } = string.Empty;
    public string PrincipalId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}

public record SetCollectionAccessRequest
{
    public string? PrincipalType { get; init; } = "user";
    public string? PrincipalId { get; init; }
    public string? Role { get; init; }
}

public record UserAccessSummaryDto
{
    public string UserId { get; init; } = string.Empty;
    public int CollectionCount { get; init; }
    public string HighestRole { get; init; } = string.Empty;
    public List<UserCollectionAccessDto> Collections { get; init; } = new();
}

public record UserCollectionAccessDto
{
    public Guid CollectionId { get; init; }
    public string CollectionName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}
