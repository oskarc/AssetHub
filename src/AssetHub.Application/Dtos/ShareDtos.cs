using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// Request DTO for creating a new share link.
/// </summary>
public class CreateShareDto
{
    /// <summary>AssetId or CollectionId being shared.</summary>
    [Required]
    public required Guid ScopeId { get; set; }

    /// <summary>"asset" or "collection".</summary>
    [Required]
    [RegularExpression("^(asset|collection)$", ErrorMessage = "ScopeType must be 'asset' or 'collection'")]
    public required string ScopeType { get; set; }

    public DateTime? ExpiresAt { get; set; }

    [StringLength(100, MinimumLength = 4, ErrorMessage = "Password must be 4-100 characters")]
    public string? Password { get; set; }

    public Dictionary<string, bool>? PermissionsJson { get; set; }

    /// <summary>
    /// Optional list of email addresses to notify about this share.
    /// Maximum 10 recipients.
    /// </summary>
    [MaxLength(10, ErrorMessage = "Maximum 10 email recipients allowed")]
    public List<string>? NotifyEmails { get; set; }
}

/// <summary>
/// Response DTO returned when a share link is created.
/// </summary>
public class ShareResponseDto
{
    public required Guid Id { get; set; }
    public required string ScopeType { get; set; }
    public required Guid ScopeId { get; set; }
    public required string Token { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public required Dictionary<string, bool> PermissionsJson { get; set; }
    public required string ShareUrl { get; set; }

    /// <summary>
    /// The plaintext password (only returned once at creation time).
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>
/// DTO representing a shared asset (used in public share responses).
/// </summary>
public class SharedAssetDto
{
    public string Type { get; set; } = Constants.ScopeTypes.Asset;
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Copyright { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? MediumUrl { get; set; }
    public Dictionary<string, object> MetadataJson { get; set; } = new();
    public Dictionary<string, bool> Permissions { get; set; } = new();
}

/// <summary>
/// DTO representing a shared collection with its assets.
/// </summary>
public class SharedCollectionDto
{
    public string Type { get; set; } = Constants.ScopeTypes.Collection;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<SharedAssetDto> Assets { get; set; } = new();
    public int TotalAssets { get; set; }
    public Dictionary<string, bool> Permissions { get; set; } = new();
}

/// <summary>
/// DTO for updating a share's password.
/// </summary>
public class UpdateSharePasswordDto
{
    [Required]
    [StringLength(100, MinimumLength = 4, ErrorMessage = "Password must be 4-100 characters")]
    public required string Password { get; set; }
}

/// <summary>
/// Response returned when a share requires password authentication.
/// </summary>
public class PasswordRequiredResponse
{
    public bool RequiresPassword { get; set; }
}

/// <summary>
/// Response returned when a share access token is successfully created.
/// The access token is a short-lived, cryptographically signed credential
/// that replaces the password in query strings (for img/video/a elements).
/// </summary>
public class ShareAccessTokenResponse
{
    public required string AccessToken { get; set; }
    public required int ExpiresInSeconds { get; set; }
}
