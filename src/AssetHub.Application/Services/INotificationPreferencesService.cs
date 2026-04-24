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

    /// <summary>
    /// Returns the full stored preferences row for a user (not wrapped in a
    /// DTO), so callers building email URLs can read the row's unsubscribe
    /// stamp. Returns null when no row exists. Used by the email-send handler.
    /// </summary>
    Task<NotificationPreferences?> GetByUserIdAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Anonymous unsubscribe flow driven by a signed token embedded in an
    /// email link. Validates the token, confirms the embedded stamp matches
    /// the current preferences row, flips <c>Email = false</c> for the
    /// embedded category, and emits a <c>notification.unsubscribed_via_email</c>
    /// audit event.
    ///
    /// Returns a 2xx-friendly shape even when the token is invalid so the
    /// endpoint can render a neutral confirmation page without leaking
    /// whether a given token is known — rate limiting at the transport layer
    /// handles the abuse surface.
    /// </summary>
    Task<ServiceResult<UnsubscribeResult>> UnsubscribeFromCategoryAsync(string token, CancellationToken ct);
}

/// <summary>
/// Outcome of the anonymous unsubscribe endpoint. <see cref="Applied"/> is
/// <c>true</c> when we actually flipped a category; <c>false</c> when the
/// token was invalid, expired, or already-unsubscribed.
/// </summary>
public sealed record UnsubscribeResult(bool Applied, string? Category);
