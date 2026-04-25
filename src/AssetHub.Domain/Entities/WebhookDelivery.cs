namespace AssetHub.Domain.Entities;

/// <summary>
/// One delivery attempt cluster for a single (Webhook, event) pair. Stays
/// pending until either the dispatcher hits a 2xx (delivered) or Wolverine
/// exhausts retries (failed). The publisher creates exactly one row per
/// matching webhook per event; the dispatcher updates it in place.
/// </summary>
public class WebhookDelivery
{
    public Guid Id { get; set; }

    public Guid WebhookId { get; set; }
    public Webhook? Webhook { get; set; }

    /// <summary>Event type token, e.g. <c>comment.created</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Serialised JSON payload as stored on disk and sent over the wire. JSONB column.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;

    /// <summary>HTTP status code from the most recent attempt; null until first attempt.</summary>
    public int? ResponseStatus { get; set; }

    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Last error message — exception type + truncated body if non-2xx. Null on success.</summary>
    public string? LastError { get; set; }
}

public enum WebhookDeliveryStatus
{
    /// <summary>Created, not yet attempted by the dispatcher.</summary>
    Pending,
    /// <summary>2xx response received.</summary>
    Delivered,
    /// <summary>4xx response or retries exhausted.</summary>
    Failed
}
