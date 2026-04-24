using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Persistence for in-app notifications. All list/read operations are scoped
/// to a single <c>UserId</c>; enforce this in the service layer so a user
/// can never see another user's notifications.
/// </summary>
public interface INotificationRepository
{
    /// <summary>Append a new notification.</summary>
    Task<Notification> CreateAsync(Notification notification, CancellationToken ct = default);

    /// <summary>List the user's notifications, newest first. <paramref name="unreadOnly"/> filters to ReadAt IS NULL.</summary>
    Task<List<Notification>> ListAsync(string userId, bool unreadOnly, int skip, int take, CancellationToken ct = default);

    /// <summary>Count the user's notifications matching the same filter.</summary>
    Task<int> CountAsync(string userId, bool unreadOnly, CancellationToken ct = default);

    /// <summary>Count unread notifications. Hot-path for the bell badge; consider caching.</summary>
    Task<int> CountUnreadAsync(string userId, CancellationToken ct = default);

    /// <summary>Get a notification by id scoped to its owner. Returns null if absent or owned by another user.</summary>
    Task<Notification?> GetForOwnerAsync(Guid id, string userId, CancellationToken ct = default);

    /// <summary>
    /// Get a notification by id without owner scoping. Use only in trusted
    /// server-side contexts (email-send handler, admin); never expose this
    /// over an endpoint without re-checking ownership.
    /// </summary>
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Stamp ReadAt on a single notification. Idempotent.</summary>
    Task<bool> MarkReadAsync(Guid id, string userId, DateTime readAt, CancellationToken ct = default);

    /// <summary>Stamp ReadAt on every unread notification for the user. Returns the number affected.</summary>
    Task<int> MarkAllReadAsync(string userId, DateTime readAt, CancellationToken ct = default);

    /// <summary>Delete a notification if it belongs to the user. Returns true if a row was removed.</summary>
    Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default);
}
