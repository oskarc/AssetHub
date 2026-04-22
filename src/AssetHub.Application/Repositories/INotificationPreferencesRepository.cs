using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Persistence for per-user notification preferences. Exactly one row per
/// user — the service lazily creates it on first read or update.
/// </summary>
public interface INotificationPreferencesRepository
{
    /// <summary>Returns the user's prefs or null if the row hasn't been created yet.</summary>
    Task<NotificationPreferences?> GetByUserIdAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Lookup by unsubscribe token hash. Used by the anonymous unsubscribe
    /// endpoint; constant-time on the unique index.
    /// </summary>
    Task<NotificationPreferences?> GetByUnsubscribeTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Insert a freshly populated row. Caller sets Id, CreatedAt, UpdatedAt, token hash.</summary>
    Task<NotificationPreferences> CreateAsync(NotificationPreferences prefs, CancellationToken ct = default);

    /// <summary>Persist mutations. Bumps UpdatedAt implicitly via the service.</summary>
    Task UpdateAsync(NotificationPreferences prefs, CancellationToken ct = default);
}
