using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<NotificationListResponse> GetNotificationsAsync(
        bool unreadOnly = false, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await notificationService.ListForCurrentUserAsync(unreadOnly, skip, take, ct);
        return Unwrap(result, "Get notifications");
    }

    public async Task<int> GetNotificationUnreadCountAsync(CancellationToken ct = default)
    {
        var result = await notificationService.GetUnreadCountForCurrentUserAsync(ct);
        return Unwrap(result, "Get notification unread count").Count;
    }

    public async Task MarkNotificationReadAsync(Guid id, CancellationToken ct = default)
    {
        var result = await notificationService.MarkReadAsync(id, ct);
        EnsureSuccess(result, "Mark notification read");
    }

    public async Task<int> MarkAllNotificationsReadAsync(CancellationToken ct = default)
    {
        var result = await notificationService.MarkAllReadForCurrentUserAsync(ct);
        return Unwrap(result, "Mark all notifications read");
    }

    public async Task DeleteNotificationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await notificationService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete notification");
    }

    public async Task<NotificationPreferencesDto> GetNotificationPreferencesAsync(CancellationToken ct = default)
    {
        var result = await notificationPreferencesService.GetForCurrentUserAsync(ct);
        return Unwrap(result, "Get notification preferences");
    }

    public async Task<NotificationPreferencesDto> UpdateNotificationPreferencesAsync(
        UpdateNotificationPreferencesDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update notification preferences");
        var result = await notificationPreferencesService.UpdateForCurrentUserAsync(dto, ct);
        return Unwrap(result, "Update notification preferences");
    }

    /// <summary>
    /// Rotates the user's unsubscribe stamp. Every email-link unsubscribe URL
    /// signed against the previous stamp stops validating immediately. Used
    /// from the Account → Notifications page when a user suspects their
    /// emails (and the unsubscribe URLs in them) have been forwarded or
    /// screenshot-shared.
    /// </summary>
    public async Task RotateUnsubscribeTokenAsync(CancellationToken ct = default)
    {
        var result = await notificationPreferencesService.RotateUnsubscribeTokenAsync(ct);
        EnsureSuccess(result, "Rotate unsubscribe token");
    }
}
