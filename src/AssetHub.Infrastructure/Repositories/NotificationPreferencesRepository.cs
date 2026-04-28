using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class NotificationPreferencesRepository(DbContextProvider provider) : INotificationPreferencesRepository
{
    public async Task<NotificationPreferences?> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }

    public async Task<NotificationPreferences?> GetByUnsubscribeTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UnsubscribeTokenHash == tokenHash, ct);
    }

    public async Task<NotificationPreferences> CreateAsync(NotificationPreferences prefs, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        lease.Db.NotificationPreferences.Add(prefs);
        await lease.Db.SaveChangesAsync(ct);
        return prefs;
    }

    public async Task UpdateAsync(NotificationPreferences prefs, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        // Force a row-level UPDATE so the JSONB Categories column is bumped even
        // when only a nested value changed (ValueComparer covers this but explicit
        // MarkAsModified is a harmless safety net). Works whether prefs was
        // tracked by an outer UoW context (ambient lease) or was detached
        // (fresh lease — Modified state attaches the row).
        lease.Db.Entry(prefs).State = EntityState.Modified;
        await lease.Db.SaveChangesAsync(ct);
    }
}
