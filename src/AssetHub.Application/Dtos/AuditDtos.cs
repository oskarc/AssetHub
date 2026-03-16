using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// Audit event for the activity feed and admin audit log.
/// </summary>
public class AuditEventDto
{
    public string EventType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string? TargetName { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}

/// <summary>
/// Request parameters for querying audit events with pagination.
/// </summary>
public class AuditQueryRequest
{
    /// <summary>Number of items per page (default: 50, max: 200).</summary>
    [Range(1, 200)]
    public int PageSize { get; set; } = 50;

    /// <summary>Cursor for pagination (CreatedAt of last item from previous page).</summary>
    public DateTime? Cursor { get; set; }

    /// <summary>Optional filter by event type (e.g., "asset.created").</summary>
    public string? EventType { get; set; }

    /// <summary>Optional filter by target type (e.g., "asset", "collection").</summary>
    public string? TargetType { get; set; }

    /// <summary>Optional filter by actor user ID.</summary>
    public string? ActorUserId { get; set; }
}

/// <summary>
/// Paginated response for audit events.
/// </summary>
public class AuditQueryResponse
{
    /// <summary>List of audit events for the current page.</summary>
    public List<AuditEventDto> Items { get; set; } = [];

    /// <summary>Total count of matching events (for display only, may be approximate for large datasets).</summary>
    public int TotalCount { get; set; }

    /// <summary>True when TotalCount has been capped at the display limit (actual count may be higher).</summary>
    public bool IsCapped { get; set; }

    /// <summary>Cursor for the next page (null if no more pages).</summary>
    public DateTime? NextCursor { get; set; }

    /// <summary>Whether there are more pages available.</summary>
    public bool HasMore { get; set; }
}
