using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Admin CRUD for outbound webhooks (T3-INT-01). All methods are
/// admin-only — the implementation enforces via <c>CurrentUser.IsSystemAdmin</c>.
/// </summary>
public interface IWebhookService
{
    Task<ServiceResult<List<WebhookResponseDto>>> ListAsync(CancellationToken ct);

    Task<ServiceResult<WebhookResponseDto>> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Creates the webhook and returns the plaintext secret <em>once</em>
    /// in <see cref="CreatedWebhookDto.PlaintextSecret"/>; storage is the
    /// encrypted form.
    /// </summary>
    Task<ServiceResult<CreatedWebhookDto>> CreateAsync(CreateWebhookDto dto, CancellationToken ct);

    Task<ServiceResult<WebhookResponseDto>> UpdateAsync(Guid id, UpdateWebhookDto dto, CancellationToken ct);

    Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Rotates the signing secret. Returns the new plaintext once, like
    /// <see cref="CreateAsync"/>. Outstanding deliveries already in flight
    /// were signed with the old secret.
    /// </summary>
    Task<ServiceResult<CreatedWebhookDto>> RotateSecretAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Fires a synthetic <c>webhook.test</c> event so admins can verify
    /// connectivity / signature handling without needing a real producer
    /// to fire. The synthetic event bypasses the subscription filter so it
    /// always reaches the target webhook even if the test event type isn't
    /// in <see cref="Domain.Entities.Webhook.EventTypes"/>.
    /// </summary>
    Task<ServiceResult<WebhookDeliveryResponseDto>> SendTestAsync(Guid id, CancellationToken ct);

    Task<ServiceResult<List<WebhookDeliveryResponseDto>>> ListDeliveriesAsync(
        Guid webhookId, int take, CancellationToken ct);
}
