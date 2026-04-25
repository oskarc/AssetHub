using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface IWebhookDeliveryRepository
{
    Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Most-recent deliveries for one webhook, newest first. Drives the
    /// admin UI's "recent deliveries" panel.
    /// </summary>
    Task<List<WebhookDelivery>> ListByWebhookAsync(
        Guid webhookId, int take, CancellationToken ct = default);

    Task<WebhookDelivery> CreateAsync(WebhookDelivery delivery, CancellationToken ct = default);

    Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct = default);
}
