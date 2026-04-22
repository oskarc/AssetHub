using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class NotificationPreferencesRepository(AssetHubDbContext db) : INotificationPreferencesRepository
{
    public async Task<NotificationPreferences?> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        return await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }

    public async Task<NotificationPreferences?> GetByUnsubscribeTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        return await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UnsubscribeTokenHash == tokenHash, ct);
    }

    public async Task<NotificationPreferences> CreateAsync(NotificationPreferences prefs, CancellationToken ct = default)
    {
        db.NotificationPreferences.Add(prefs);
        await db.SaveChangesAsync(ct);
        return prefs;
    }

    public async Task UpdateAsync(NotificationPreferences prefs, CancellationToken ct = default)
    {
        // Entity is tracked via the GetByUserIdAsync call the service made.
        // Force a row-level UPDATE so the JSONB Categories column is bumped even
        // when only a nested value changed (ValueComparer covers this but explicit
        // MarkAsModified is a harmless safety net).
        db.Entry(prefs).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }
}
