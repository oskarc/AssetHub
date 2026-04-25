using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class WebhookDeliveryRepository(AssetHubDbContext db) : IWebhookDeliveryRepository
{
    public async Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<List<WebhookDelivery>> ListByWebhookAsync(
        Guid webhookId, int take, CancellationToken ct = default)
    {
        return await db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.WebhookId == webhookId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<WebhookDelivery> CreateAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        if (delivery.Id == Guid.Empty) delivery.Id = Guid.NewGuid();
        if (delivery.CreatedAt == default) delivery.CreatedAt = DateTime.UtcNow;
        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
        return delivery;
    }

    public async Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        db.WebhookDeliveries.Update(delivery);
        await db.SaveChangesAsync(ct);
    }
}
