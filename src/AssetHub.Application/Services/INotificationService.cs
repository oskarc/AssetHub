using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Produces and reads in-app notifications. Other features (saved-search
/// digests, mentions, workflow) call <see cref="CreateAsync"/>; the bell UI
/// calls the list / mark-read / delete methods against the current user.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Create a notification for <paramref name="userId"/>, respecting that
    /// user's preferences for <paramref name="category"/>. Returns the
    /// persisted row if the user has in-app enabled for the category, or
    /// <c>null</c> if suppressed. Email delivery is handled separately in
    /// phase 3.
    /// </summary>
    Task<ServiceResult<NotificationDto?>> CreateAsync(
        string userId, string category, string title,
        string? body = null, string? url = null,
        Dictionary<string, object>? data = null,
        CancellationToken ct = default);

    /// <summary>List for the current user (scoped via <c>CurrentUser</c>), newest first.</summary>
    Task<ServiceResult<NotificationListResponse>> ListForCurrentUserAsync(
        bool unreadOnly, int skip, int take, CancellationToken ct);

    /// <summary>Unread count for the current user. Cached; invalidated on create/mark-read/delete.</summary>
    Task<ServiceResult<NotificationUnreadCountDto>> GetUnreadCountForCurrentUserAsync(CancellationToken ct);

    /// <summary>Mark one notification read. 404 if it doesn't belong to the current user.</summary>
    Task<ServiceResult> MarkReadAsync(Guid notificationId, CancellationToken ct);

    /// <summary>Mark every unread notification for the current user as read. Returns the count affected.</summary>
    Task<ServiceResult<int>> MarkAllReadForCurrentUserAsync(CancellationToken ct);

    /// <summary>Permanently delete one notification. 404 if it doesn't belong to the current user.</summary>
    Task<ServiceResult> DeleteAsync(Guid notificationId, CancellationToken ct);
}
