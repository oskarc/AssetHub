using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class WebhookRepository(DbContextProvider provider) : IWebhookRepository
{
    public async Task<List<Webhook>> ListAllAsync(CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Webhooks.AsNoTracking().OrderByDescending(w => w.CreatedAt).ToListAsync(ct);
    }

    public async Task<Webhook?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<List<Webhook>> ListActiveSubscribedAsync(string eventType, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        // Postgres array containment via EF — `text[] && text[]` returns true
        // when any element overlaps. We feed a single-element array so it
        // matches when EventTypes contains the requested type.
        return await db.Webhooks
            .AsNoTracking()
            .Where(w => w.IsActive && w.EventTypes.Contains(eventType))
            .ToListAsync(ct);
    }

    public async Task<Webhook> CreateAsync(Webhook webhook, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        if (webhook.Id == Guid.Empty) webhook.Id = Guid.NewGuid();
        if (webhook.CreatedAt == default) webhook.CreatedAt = DateTime.UtcNow;
        db.Webhooks.Add(webhook);
        await db.SaveChangesAsync(ct);
        return webhook;
    }

    public async Task UpdateAsync(Webhook webhook, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        db.Webhooks.Update(webhook);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var rows = await db.Webhooks.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0;
    }
}
