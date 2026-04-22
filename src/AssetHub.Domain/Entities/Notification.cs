namespace AssetHub.Domain.Entities;

/// <summary>
/// A single notification targeted at one user. In-app notifications are stored
/// in this table; email delivery is a separate concern driven by the user's
/// <see cref="NotificationPreferences"/> at creation time.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    /// <summary>Keycloak sub of the recipient. Indexed on (UserId, CreatedAt DESC) for the bell list.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Category key (e.g. "mention", "saved_search_digest", "migration_completed"). Matches the preferences map.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Short human-readable title shown in the bell dropdown.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional longer body shown in the full notifications list. No HTML — plain text only.</summary>
    public string? Body { get; set; }

    /// <summary>Optional app-internal URL to deep-link to (e.g., "/assets/{id}"). Clicking the notification navigates here and marks it read.</summary>
    public string? Url { get; set; }

    /// <summary>Category-specific payload for future renderers (e.g., mention → commenter id; digest → match count). JSONB.</summary>
    public Dictionary<string, object> Data { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    /// <summary>Null when unread; stamped when the user marks the notification read.</summary>
    public DateTime? ReadAt { get; set; }
}
