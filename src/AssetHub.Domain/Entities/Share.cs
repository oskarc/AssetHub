namespace AssetHub.Domain.Entities;

public class Share
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = string.Empty; // SHA256(token)
    /// <summary>
    /// Encrypted plaintext token stored so admins can retrieve the original share link when necessary.
    /// This is protected using the ASP.NET Core Data Protection APIs and should only be readable by admins.
    /// </summary>
    public string? TokenEncrypted { get; set; }
    public ShareScopeType ScopeType { get; set; }
    public Guid ScopeId { get; set; }
    public Dictionary<string, bool> PermissionsJson { get; set; } = new();
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? PasswordHash { get; set; }
    /// <summary>
    /// Encrypted plaintext password stored so admins can retrieve it when necessary.
    /// This is protected using the ASP.NET Core Data Protection APIs and should only be readable by admins.
    /// </summary>
    public string? PasswordEncrypted { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }

    // Navigation
    public Asset? Asset { get; set; }
    public Collection? Collection { get; set; }
}
