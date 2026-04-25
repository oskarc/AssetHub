using System.Net;
using System.Security.Cryptography;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Handlers;

/// <summary>
/// Sends a single <see cref="WebhookDelivery"/> over HTTP, signed with
/// HMAC-SHA256 using the webhook's encrypted secret. Wolverine's outer
/// retry policy (5-step cooldown) drives transient retries; permanent
/// failures (4xx or retries exhausted) flip the row to
/// <see cref="WebhookDeliveryStatus.Failed"/> and audit the loss.
///
/// Distinguishing transient vs permanent:
/// <list type="bullet">
/// <item>2xx → <see cref="WebhookDeliveryStatus.Delivered"/>, no retry.</item>
/// <item>4xx → <see cref="WebhookDeliveryStatus.Failed"/>, no retry. Webhook
/// receivers signal "stop sending this" with 4xx.</item>
/// <item>5xx / network → throw, let Wolverine retry. After exhaustion the
/// next handler call sees <c>AttemptCount</c> ≥ max and marks Failed.</item>
/// </list>
/// </summary>
public sealed class DispatchWebhookHandler(
    IWebhookDeliveryRepository deliveries,
    IWebhookRepository webhooks,
    IWebhookSecretProtector protector,
    IAuditService audit,
    IHttpClientFactory httpFactory,
    ILogger<DispatchWebhookHandler> logger)
{
    /// <summary>Wolverine's policy retries 5 times; 6 attempts total is the cap.</summary>
    public const int MaxAttempts = 6;

    public async Task HandleAsync(DispatchWebhookCommand command, CancellationToken ct)
    {
        var delivery = await deliveries.GetByIdAsync(command.DeliveryId, ct);
        if (delivery is null)
        {
            logger.LogDebug(
                "Webhook delivery {DeliveryId} no longer exists; skipping",
                command.DeliveryId);
            return;
        }

        if (delivery.Status == WebhookDeliveryStatus.Delivered)
        {
            // Idempotency: a duplicate Wolverine redelivery after success
            // shouldn't ping the receiver again.
            return;
        }

        var webhook = await webhooks.GetByIdAsync(delivery.WebhookId, ct);
        if (webhook is null)
        {
            // Webhook was deleted between publish and dispatch — record
            // and skip. Cascade should normally catch this but the row
            // can also exist if the cascade hasn't propagated yet.
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.LastError = "Webhook configuration was deleted before delivery.";
            delivery.LastAttemptAt = DateTime.UtcNow;
            await deliveries.UpdateAsync(delivery, ct);
            return;
        }

        delivery.AttemptCount++;
        delivery.LastAttemptAt = DateTime.UtcNow;

        try
        {
            using var client = httpFactory.CreateClient("webhook-dispatch");
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url);

            var payloadBytes = Encoding.UTF8.GetBytes(delivery.PayloadJson);
            request.Content = new ByteArrayContent(payloadBytes);
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
                {
                    CharSet = "utf-8"
                };

            var signature = ComputeSignature(payloadBytes, protector.Unprotect(webhook.SecretEncrypted));
            request.Headers.Add("X-AssetHub-Signature", $"sha256={signature}");
            request.Headers.Add("X-AssetHub-Event", delivery.EventType);
            request.Headers.Add("X-AssetHub-Delivery", delivery.Id.ToString());
            request.Headers.UserAgent.ParseAdd("AssetHub-Webhooks/1.0");

            using var response = await client.SendAsync(request, ct);
            delivery.ResponseStatus = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = WebhookDeliveryStatus.Delivered;
                delivery.DeliveredAt = DateTime.UtcNow;
                delivery.LastError = null;
                await deliveries.UpdateAsync(delivery, ct);

                logger.LogInformation(
                    "Webhook delivered: {DeliveryId} → {WebhookId} ({EventType}) status {Status}",
                    delivery.Id, webhook.Id, delivery.EventType, delivery.ResponseStatus);
                return;
            }

            // 4xx: receiver explicitly rejecting — don't retry.
            if (response.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                delivery.Status = WebhookDeliveryStatus.Failed;
                delivery.LastError = await TruncatedBodyAsync(response, ct);
                await deliveries.UpdateAsync(delivery, ct);
                await AuditPermanentFailureAsync(delivery, webhook, ct);
                return;
            }

            // 5xx: throw to let Wolverine retry. Update row first so the
            // attempt count and error stick if the row is read mid-retry.
            delivery.LastError = await TruncatedBodyAsync(response, ct);
            await deliveries.UpdateAsync(delivery, ct);

            if (delivery.AttemptCount >= MaxAttempts)
            {
                delivery.Status = WebhookDeliveryStatus.Failed;
                await deliveries.UpdateAsync(delivery, ct);
                await AuditPermanentFailureAsync(delivery, webhook, ct);
                return;
            }

            throw new HttpRequestException(
                $"Webhook receiver returned {(int)response.StatusCode}; will retry.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            delivery.LastError = $"{ex.GetType().Name}: {Truncate(ex.Message, 1500)}";
            await deliveries.UpdateAsync(delivery, ct);

            if (delivery.AttemptCount >= MaxAttempts)
            {
                delivery.Status = WebhookDeliveryStatus.Failed;
                await deliveries.UpdateAsync(delivery, ct);
                await AuditPermanentFailureAsync(delivery, webhook, ct);
                return;
            }

            // Re-throw — Wolverine's RetryWithCooldown picks it up.
            throw;
        }
    }

    private static string ComputeSignature(byte[] payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(hmac.ComputeHash(payload));
    }

    private static async Task<string> TruncatedBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return $"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 1500)}";
        }
        catch
        {
            return $"{(int)response.StatusCode} {response.ReasonPhrase}";
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private async Task AuditPermanentFailureAsync(
        WebhookDelivery delivery, Webhook webhook, CancellationToken ct)
    {
        await audit.LogAsync(
            NotificationConstants.AuditEvents.WebhookDeliveryFailedPermanently,
            Constants.ScopeTypes.WebhookDelivery,
            delivery.Id,
            actorUserId: null,
            new Dictionary<string, object>
            {
                ["webhook_id"] = delivery.WebhookId,
                ["event_type"] = delivery.EventType,
                ["endpoint_host"] = SafeHost(webhook.Url),
                ["attempt_count"] = delivery.AttemptCount,
                ["response_status"] = (object?)delivery.ResponseStatus ?? string.Empty
            },
            ct);

        logger.LogWarning(
            "Webhook delivery {DeliveryId} → {WebhookId} ({EventType}) permanently failed after {Attempts} attempts",
            delivery.Id, delivery.WebhookId, delivery.EventType, delivery.AttemptCount);
    }

    private static string SafeHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "(invalid)";
}
