using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>Serialised view of a single notification for the bell / list UI.</summary>
public class NotificationDto
{
    public required Guid Id { get; set; }
    public required string Category { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public required DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public bool IsRead => ReadAt.HasValue;
}

/// <summary>Paginated list payload.</summary>
public class NotificationListResponse
{
    public required List<NotificationDto> Items { get; set; }
    public required int TotalCount { get; set; }
    public required int UnreadCount { get; set; }
}

/// <summary>Lightweight unread-count payload for the bell badge polling endpoint.</summary>
public class NotificationUnreadCountDto
{
    public required int Count { get; set; }
}

/// <summary>Current-user preferences view.</summary>
public class NotificationPreferencesDto
{
    /// <summary>Category key → per-channel settings. Includes defaults for every known category.</summary>
    public required Dictionary<string, NotificationCategoryPrefsDto> Categories { get; set; }
}

/// <summary>Per-category channel settings (DTO mirror of the entity shape).</summary>
public class NotificationCategoryPrefsDto
{
    public bool InApp { get; set; } = true;
    public bool Email { get; set; } = true;

    /// <summary>One of "instant", "daily", "weekly".</summary>
    [Required]
    [RegularExpression("^(instant|daily|weekly)$")]
    public string EmailCadence { get; set; } = "instant";
}

/// <summary>Mutation DTO. Omitted keys keep their current value; sending an empty dict is a no-op.</summary>
public class UpdateNotificationPreferencesDto
{
    /// <summary>Category key → new settings for that category.</summary>
    [Required]
    public required Dictionary<string, NotificationCategoryPrefsDto> Categories { get; set; }
}
