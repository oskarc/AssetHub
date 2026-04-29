using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for webhook admin: repo + deliveries + secret protector + event publisher + audit + UnitOfWork + scoped CurrentUser + logger. UnitOfWork added to wrap action+audit atomically (A-4).")]
public sealed class WebhookService(
    IWebhookRepository repo,
    IWebhookDeliveryRepository deliveries,
    IWebhookSecretProtector protector,
    Wolverine.IMessageBus messageBus,
    IAuditService audit,
    IUnitOfWork uow,
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
        // Webhook insert + audit are atomic — torn write would leave a
        // subscription that fires events with no audit trail (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repo.CreateAsync(entity, tct);
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
                tct);
        }, ct);

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

        // Update + audit atomic (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repo.UpdateAsync(row, tct);
            await audit.LogAsync(
                NotificationConstants.AuditEvents.WebhookUpdated,
                Constants.ScopeTypes.Webhook,
                id, currentUser.UserId,
                new Dictionary<string, object>
                {
                    ["changed_fields"] = changed,
                    [EndpointHostKey] = SafeHost(row.Url)
                },
                tct);
        }, ct);

        return ToDto(row);
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var existing = await repo.GetByIdAsync(id, ct);
        if (existing is null) return ServiceError.NotFound(WebhookNotFound);

        // Delete + audit atomic (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repo.DeleteAsync(id, tct);
            await audit.LogAsync(
                NotificationConstants.AuditEvents.WebhookDeleted,
                Constants.ScopeTypes.Webhook,
                id, currentUser.UserId,
                new Dictionary<string, object>
                {
                    [EndpointHostKey] = SafeHost(existing.Url),
                    ["name"] = existing.Name
                },
                tct);
        }, ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<CreatedWebhookDto>> RotateSecretAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(WebhookNotFound);

        var plaintext = protector.GeneratePlaintext();
        row.SecretEncrypted = protector.Protect(plaintext);

        // Secret rotation + audit atomic — without this, a torn write
        // could leave the row pointing at a new secret with no audit
        // trail of who rotated it (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repo.UpdateAsync(row, tct);
            await audit.LogAsync(
                NotificationConstants.AuditEvents.WebhookSecretRotated,
                Constants.ScopeTypes.Webhook,
                id, currentUser.UserId,
                new Dictionary<string, object>
                {
                    [EndpointHostKey] = SafeHost(row.Url)
                },
                tct);
        }, ct);

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

        // Test events deliberately bypass the publisher's subscription filter
        // (it would skip every webhook because no webhook subscribes to
        // "webhook.test" — that's a synthetic admin-side ping, not an event
        // type a user adds to EventTypes). Persist the delivery row using the
        // SAME envelope shape the publisher writes for real events, then
        // enqueue DispatchWebhookCommand directly so the worker picks it up.
        var deliveryId = Guid.NewGuid();
        const string testEventType = "webhook.test";
        var envelope = new
        {
            id = deliveryId,
            type = testEventType,
            createdAt = DateTime.UtcNow,
            data = payload
        };
        var delivery = new WebhookDelivery
        {
            Id = deliveryId,
            WebhookId = row.Id,
            EventType = testEventType,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(envelope),
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await deliveries.CreateAsync(delivery, ct);
        await messageBus.PublishAsync(new Application.Messages.DispatchWebhookCommand
        {
            DeliveryId = deliveryId
        });

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
        => Application.Helpers.OutboundUrlGuard.IsSafeOutboundUrl(raw, out error);

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
