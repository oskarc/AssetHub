using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class WebhookService(
    IWebhookRepository repo,
    IWebhookDeliveryRepository deliveries,
    IWebhookSecretProtector protector,
    IWebhookEventPublisher publisher,
    IAuditService audit,
    CurrentUser currentUser,
    ILogger<WebhookService> logger) : IWebhookService
{
    private const int DefaultDeliveriesPageSize = 50;
    private const int MaxDeliveriesPageSize = 200;
    private const string WebhookNotFound = "Webhook not found.";
    private const string EndpointHostKey = "endpoint_host";

    public async Task<ServiceResult<List<WebhookResponseDto>>> ListAsync(CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();
        var rows = await repo.ListAllAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<WebhookResponseDto>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();
        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(WebhookNotFound);
        return ToDto(row);
    }

    public async Task<ServiceResult<CreatedWebhookDto>> CreateAsync(CreateWebhookDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var unknown = dto.EventTypes.Where(t => !WebhookEvents.IsKnown(t)).ToList();
        if (unknown.Count > 0)
            return ServiceError.Validation(
                "One or more event types are not recognised.",
                unknown.ToDictionary(t => $"eventTypes.{t}", _ => "Unknown event type."));

        if (!IsValidUrl(dto.Url, out var urlError))
            return ServiceError.BadRequest(urlError!);

        var plaintext = protector.GeneratePlaintext();
        var entity = new Webhook
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Url = dto.Url.Trim(),
            SecretEncrypted = protector.Protect(plaintext),
            EventTypes = dto.EventTypes.Distinct(StringComparer.Ordinal).ToList(),
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        };
        await repo.CreateAsync(entity, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.WebhookCreated,
            Constants.ScopeTypes.Webhook,
            entity.Id,
            currentUser.UserId,
            new Dictionary<string, object>
            {
                [EndpointHostKey] = SafeHost(entity.Url),
                ["event_types"] = entity.EventTypes
            },
            ct);

        logger.LogInformation(
            "Webhook {WebhookId} '{Name}' created by {UserId} subscribed to {Events}",
            entity.Id, entity.Name, currentUser.UserId, string.Join(",", entity.EventTypes));

        return new CreatedWebhookDto { Webhook = ToDto(entity), PlaintextSecret = plaintext };
    }

    public async Task<ServiceResult<WebhookResponseDto>> UpdateAsync(
        Guid id, UpdateWebhookDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(WebhookNotFound);

        var changed = new List<string>();

        if (dto.Name is not null && dto.Name != row.Name)
        {
            row.Name = dto.Name.Trim();
            changed.Add(nameof(row.Name));
        }
        if (dto.Url is not null && dto.Url != row.Url)
        {
            if (!IsValidUrl(dto.Url, out var urlError))
                return ServiceError.BadRequest(urlError!);
            row.Url = dto.Url.Trim();
            changed.Add(nameof(row.Url));
        }
        if (dto.EventTypes is not null)
        {
            var unknown = dto.EventTypes.Where(t => !WebhookEvents.IsKnown(t)).ToList();
            if (unknown.Count > 0)
                return ServiceError.Validation(
                    "One or more event types are not recognised.",
                    unknown.ToDictionary(t => $"eventTypes.{t}", _ => "Unknown event type."));
            row.EventTypes = dto.EventTypes.Distinct(StringComparer.Ordinal).ToList();
            changed.Add(nameof(row.EventTypes));
        }
        if (dto.IsActive is bool active && active != row.IsActive)
        {
            row.IsActive = active;
            changed.Add(nameof(row.IsActive));
        }

        if (changed.Count == 0) return ToDto(row);

        await repo.UpdateAsync(row, ct);
        await audit.LogAsync(
            NotificationConstants.AuditEvents.WebhookUpdated,
            Constants.ScopeTypes.Webhook,
            id, currentUser.UserId,
            new Dictionary<string, object>
            {
                ["changed_fields"] = changed,
                [EndpointHostKey] = SafeHost(row.Url)
            },
            ct);

        return ToDto(row);
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var existing = await repo.GetByIdAsync(id, ct);
        if (existing is null) return ServiceError.NotFound(WebhookNotFound);

        await repo.DeleteAsync(id, ct);
        await audit.LogAsync(
            NotificationConstants.AuditEvents.WebhookDeleted,
            Constants.ScopeTypes.Webhook,
            id, currentUser.UserId,
            new Dictionary<string, object>
            {
                [EndpointHostKey] = SafeHost(existing.Url),
                ["name"] = existing.Name
            },
            ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<CreatedWebhookDto>> RotateSecretAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(WebhookNotFound);

        var plaintext = protector.GeneratePlaintext();
        row.SecretEncrypted = protector.Protect(plaintext);
        await repo.UpdateAsync(row, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.WebhookSecretRotated,
            Constants.ScopeTypes.Webhook,
            id, currentUser.UserId,
            new Dictionary<string, object>
            {
                [EndpointHostKey] = SafeHost(row.Url)
            },
            ct);

        return new CreatedWebhookDto { Webhook = ToDto(row), PlaintextSecret = plaintext };
    }

    public async Task<ServiceResult<WebhookDeliveryResponseDto>> SendTestAsync(
        Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(WebhookNotFound);

        var payload = new
        {
            message = "AssetHub webhook connectivity test",
            triggeredByUserId = currentUser.UserId,
            triggeredAt = DateTime.UtcNow
        };

        // Test events bypass the subscription filter — we want to ping
        // *this specific* webhook regardless of its EventTypes list.
        var delivery = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookId = row.Id,
            EventType = "webhook.test",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = (Guid?)null,
                type = "webhook.test",
                createdAt = DateTime.UtcNow,
                data = payload
            }),
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await deliveries.CreateAsync(delivery, ct);
        await publisher.PublishAsync("webhook.test", payload, ct);

        return ToDto(delivery);
    }

    public async Task<ServiceResult<List<WebhookDeliveryResponseDto>>> ListDeliveriesAsync(
        Guid webhookId, int take, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();
        var clampedTake = take <= 0 ? DefaultDeliveriesPageSize : Math.Min(take, MaxDeliveriesPageSize);
        var rows = await deliveries.ListByWebhookAsync(webhookId, clampedTake, ct);
        return rows.Select(ToDto).ToList();
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static bool IsValidUrl(string raw, out string? error)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            error = "Url must be an absolute URL.";
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            error = "Only http and https schemes are accepted.";
            return false;
        }
        error = null;
        return true;
    }

    private static string SafeHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "(invalid)";

    private static WebhookResponseDto ToDto(Webhook w) => new()
    {
        Id = w.Id,
        Name = w.Name,
        Url = w.Url,
        EventTypes = w.EventTypes.ToList(),
        IsActive = w.IsActive,
        CreatedAt = w.CreatedAt,
        CreatedByUserId = w.CreatedByUserId
    };

    private static WebhookDeliveryResponseDto ToDto(WebhookDelivery d) => new()
    {
        Id = d.Id,
        WebhookId = d.WebhookId,
        EventType = d.EventType,
        Status = d.Status.ToDbString(),
        ResponseStatus = d.ResponseStatus,
        AttemptCount = d.AttemptCount,
        CreatedAt = d.CreatedAt,
        DeliveredAt = d.DeliveredAt,
        LastAttemptAt = d.LastAttemptAt,
        LastError = d.LastError
    };
}
