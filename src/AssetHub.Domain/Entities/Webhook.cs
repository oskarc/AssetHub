namespace AssetHub.Domain.Entities;

/// <summary>
/// Outbound HTTP integration (T3-INT-01). Each webhook subscribes to a list
/// of event types; when a matching event is produced, a
/// <see cref="WebhookDelivery"/> row is appended and a Wolverine command is
/// published to the dispatcher.
///
/// The signing secret is stored encrypted at rest via Data Protection
/// (<see cref="SecretEncrypted"/>) — the dispatcher needs plaintext at
/// HMAC-sign time, so this can't be a one-way hash. Plaintext is shown to
/// the admin once at creation, like Personal Access Tokens.
/// </summary>
public class Webhook
{
    public Guid Id { get; set; }

    /// <summary>Admin-supplied display name for the integration ("Slack alerts", "Zapier").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>HTTPS endpoint that receives POSTs.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Encrypted signing secret. Decrypted by the dispatcher to compute
    /// HMAC-SHA256 of the request body, surfaced as
    /// <c>X-AssetHub-Signature</c>. Never returned to clients after creation.
    /// </summary>
    public string SecretEncrypted { get; set; } = string.Empty;

    /// <summary>
    /// Event-type names this webhook subscribes to (e.g. <c>asset.created</c>,
    /// <c>workflow.state_changed</c>). Empty list = subscribe to nothing
    /// (effectively a soft-disable distinct from <see cref="IsActive"/>).
    /// </summary>
    public List<string> EventTypes { get; set; } = new();

    /// <summary>When false, the publisher skips this webhook entirely.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
}
