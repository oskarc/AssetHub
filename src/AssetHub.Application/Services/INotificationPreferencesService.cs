using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;

namespace AssetHub.Application.Services;

/// <summary>
/// Read / update for per-user notification preferences. Lazy-creates the row
/// on first access so new users always get sensible defaults (in-app on,
/// email on, instant cadence) for every known category.
/// </summary>
public interface INotificationPreferencesService
{
    /// <summary>
    /// Returns the current user's preferences, with defaults filled in for any
    /// category not yet persisted. Lazily creates the row on first call.
    /// </summary>
    Task<ServiceResult<NotificationPreferencesDto>> GetForCurrentUserAsync(CancellationToken ct);

    /// <summary>
    /// Merge-update the current user's preferences. Only categories present
    /// in the DTO are changed; omitted categories keep their stored values.
    /// Emits a <c>notification.preferences_updated</c> audit event listing the
    /// changed category keys.
    /// </summary>
    Task<ServiceResult<NotificationPreferencesDto>> UpdateForCurrentUserAsync(
        UpdateNotificationPreferencesDto dto, CancellationToken ct);

    /// <summary>
    /// Internal resolution path used by <see cref="INotificationService"/> at
    /// create time. Returns the effective prefs for a user (with defaults
    /// filled in). Does not create a row.
    /// </summary>
    Task<NotificationCategoryPrefs> ResolveForUserAsync(string userId, string category, CancellationToken ct);
}
