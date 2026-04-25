using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface IWebhookRepository
{
    Task<List<Webhook>> ListAllAsync(CancellationToken ct = default);

    Task<Webhook?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Active webhooks subscribed to the given event type. Used by
    /// <c>IWebhookEventPublisher</c> to fan an event out at publish time.
    /// </summary>
    Task<List<Webhook>> ListActiveSubscribedAsync(string eventType, CancellationToken ct = default);

    Task<Webhook> CreateAsync(Webhook webhook, CancellationToken ct = default);

    Task UpdateAsync(Webhook webhook, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
