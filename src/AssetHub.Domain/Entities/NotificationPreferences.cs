namespace AssetHub.Domain.Entities;

/// <summary>
/// Per-user notification preferences. One row per Keycloak user (indexed unique
/// on UserId). The <see cref="Categories"/> JSONB lets us add new notification
/// categories without a schema migration — missing keys fall back to sensible
/// defaults (in-app on, email on, instant).
/// </summary>
public class NotificationPreferences
{
    public Guid Id { get; set; }

    /// <summary>Keycloak sub. Unique index.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Category key → per-channel settings. JSONB. Absent keys use defaults.</summary>
    public Dictionary<string, NotificationCategoryPrefs> Categories { get; set; } = new();

    /// <summary>
    /// SHA-256 hash of the user's unsubscribe token. The plaintext is embedded
    /// in email unsubscribe URLs; the endpoint hashes the incoming token and
    /// looks up the row. Stable across preference updates so old email links
    /// keep working until the user rotates them.
    /// </summary>
    public string UnsubscribeTokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Per-category channel settings. Stored inside <see cref="NotificationPreferences.Categories"/>.</summary>
public class NotificationCategoryPrefs
{
    /// <summary>Show in the bell dropdown + notifications page. Default on.</summary>
    public bool InApp { get; set; } = true;

    /// <summary>Deliver by email. Default on. Overridden to false for the entire account via global unsubscribe.</summary>
    public bool Email { get; set; } = true;

    /// <summary>Email delivery cadence: "instant" (default), "daily", or "weekly" digest.</summary>
    public string EmailCadence { get; set; } = "instant";
}
