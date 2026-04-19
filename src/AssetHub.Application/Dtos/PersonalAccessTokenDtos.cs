using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// Request payload to mint a new PAT. The plaintext token is returned exactly once
/// in <see cref="CreatedPersonalAccessTokenDto"/> — the user is expected to copy it
/// at that moment, since the server only persists the SHA-256 hash.
/// </summary>
public class CreatePersonalAccessTokenRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute expiry. Null = never expires (until revoked).
    /// Must be in the future when supplied.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Coarse capability list. Empty = full owner-impersonation. Each scope is
    /// validated against <see cref="PersonalAccessTokenDto.AllowedScopes"/> by the service.
    /// </summary>
    [MaxLength(20)]
    public List<string> Scopes { get; set; } = new();
}

/// <summary>
/// Response shown once on successful PAT creation. Carries the plaintext bearer
/// token alongside the persistent metadata; later GETs only return the metadata.
/// </summary>
public class CreatedPersonalAccessTokenDto
{
    public required PersonalAccessTokenDto Token { get; init; }

    /// <summary>
    /// The plaintext token in the form `pat_{base64url}`. Use as `Authorization: Bearer {value}`.
    /// Shown once — we cannot recover it after this response.
    /// </summary>
    public required string PlaintextToken { get; init; }
}

/// <summary>
/// Safe-to-display PAT metadata. Never carries the plaintext token or the hash.
/// </summary>
public class PersonalAccessTokenDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string OwnerUserId { get; init; }
    public required List<string> Scopes { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }

    /// <summary>True when the token has not been revoked and has not expired.</summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Coarse scopes the API recognises. Used for validation on create and for the
    /// `[RequireScope]` filter on individual endpoints. Empty scope list on a PAT
    /// means full owner-impersonation — these constants only apply when narrowing.
    /// </summary>
    public static readonly IReadOnlyCollection<string> AllowedScopes = new[]
    {
        "assets:read",
        "assets:write",
        "collections:read",
        "collections:write",
        "shares:read",
        "shares:write",
        "search:read",
        "admin"
    };
}
