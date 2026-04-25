namespace AssetHub.Application.Messages;

/// <summary>
/// Wolverine command to dispatch a single
/// <see cref="Domain.Entities.WebhookDelivery"/>. The handler is in the
/// Worker project and signs the payload with HMAC-SHA256 before sending.
/// Wolverine's outer retry policy (5-step cooldown) handles transient
/// failures; permanent failures are recorded on the row and audit-logged.
/// </summary>
public record DispatchWebhookCommand
{
    public Guid DeliveryId { get; init; }
}
