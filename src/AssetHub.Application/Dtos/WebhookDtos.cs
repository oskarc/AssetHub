using System.ComponentModel.DataAnnotations;
using AssetHub.Application.Validation;

namespace AssetHub.Application.Dtos;

public class CreateWebhookDto
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, Url, StringLength(2048)]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Event-type tokens this webhook subscribes to. Validated against
    /// <see cref="WebhookEvents.All"/>; unknown types reject the request so
    /// admins can't pre-subscribe to events that don't exist yet.
    /// </summary>
    [Required, MaxItems(50)]
    public List<string> EventTypes { get; set; } = new();

    public bool IsActive { get; set; } = true;
}

public class UpdateWebhookDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    [Url, StringLength(2048)]
    public string? Url { get; set; }

    [MaxItems(50)]
    public List<string>? EventTypes { get; set; }

    public bool? IsActive { get; set; }
}

public class WebhookResponseDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public required List<string> EventTypes { get; set; }
    public required bool IsActive { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string CreatedByUserId { get; set; }
}

/// <summary>
/// Returned once at creation / rotation. Plaintext secret is never
/// re-derivable from <see cref="Webhook.SecretEncrypted"/> after this
/// response — admins must persist it on their side.
/// </summary>
public class CreatedWebhookDto
{
    public required WebhookResponseDto Webhook { get; set; }

    /// <summary>
    /// Plaintext signing secret. Shown once. Admins put this in the
    /// receiving system's HMAC-verification config.
    /// </summary>
    public required string PlaintextSecret { get; set; }
}

public class WebhookDeliveryResponseDto
{
    public required Guid Id { get; set; }
    public required Guid WebhookId { get; set; }
    public required string EventType { get; set; }
    public required string Status { get; set; }
    public int? ResponseStatus { get; set; }
    public required int AttemptCount { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
