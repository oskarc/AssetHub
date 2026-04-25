using System.Text.Json;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class WebhookEventPublisher(
    IWebhookRepository repo,
    IWebhookDeliveryRepository deliveries,
    IMessageBus messageBus,
    ILogger<WebhookEventPublisher> logger) : IWebhookEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
    {
        try
        {
            var subscribers = await repo.ListActiveSubscribedAsync(eventType, ct);
            if (subscribers.Count == 0)
            {
                logger.LogDebug(
                    "Webhook publish skipped: no active subscribers for {EventType}",
                    eventType);
                return;
            }

            // Standard envelope: per-delivery id is set after the row is
            // persisted, so subscribers with replay-attack guards can use
            // it as an idempotency key.
            foreach (var webhook in subscribers)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var deliveryId = Guid.NewGuid();
                    var envelope = new
                    {
                        id = deliveryId,
                        type = eventType,
                        createdAt = DateTime.UtcNow,
                        data = payload
                    };
                    var json = JsonSerializer.Serialize(envelope, JsonOptions);

                    await deliveries.CreateAsync(new WebhookDelivery
                    {
                        Id = deliveryId,
                        WebhookId = webhook.Id,
                        EventType = eventType,
                        PayloadJson = json,
                        Status = WebhookDeliveryStatus.Pending,
                        CreatedAt = DateTime.UtcNow
                    }, ct);

                    await messageBus.PublishAsync(new DispatchWebhookCommand { DeliveryId = deliveryId });
                }
                catch (Exception inner) when (inner is not OperationCanceledException)
                {
                    // One bad subscriber shouldn't block the others. Log
                    // and continue — the audit/log surface tells us where
                    // the publisher itself is failing.
                    logger.LogWarning(inner,
                        "Failed to enqueue webhook delivery for webhook {WebhookId} / event {EventType}",
                        webhook.Id, eventType);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per the contract, publisher failures must NOT abort the
            // producer's primary operation. Same shape as IAuditService.
            logger.LogError(ex,
                "Webhook publisher failed for event {EventType}; continuing",
                eventType);
        }
    }
}
