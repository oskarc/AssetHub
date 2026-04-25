namespace AssetHub.Application.Services;

/// <summary>
/// Source-side fan-out for webhook events (T3-INT-01). Producer services
/// (asset, share, comment, workflow) call this once per business event;
/// the implementation looks up active subscribers for the event type,
/// persists a <c>WebhookDelivery</c> per subscriber, and publishes a
/// Wolverine command so the worker can deliver out-of-band.
///
/// Failure here must NEVER abort the producer's primary operation —
/// implementations log and swallow exceptions, same contract as
/// <c>IAuditService</c>.
/// </summary>
public interface IWebhookEventPublisher
{
    /// <summary>
    /// <paramref name="eventType"/> is one of <see cref="WebhookEvents"/>;
    /// <paramref name="payload"/> is serialised verbatim into the
    /// <c>data</c> property of the standard envelope.
    /// </summary>
    Task PublishAsync(string eventType, object payload, CancellationToken ct = default);
}
