using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class NotificationService(
    INotificationRepository repo,
    INotificationPreferencesService preferences,
    CurrentUser currentUser,
    HybridCache cache,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task<ServiceResult<NotificationDto?>> CreateAsync(
        string userId, string category, string title,
        string? body = null, string? url = null,
        Dictionary<string, object>? data = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return ServiceError.BadRequest("userId is required.");
        if (string.IsNullOrWhiteSpace(category))
            return ServiceError.BadRequest("category is required.");
        if (string.IsNullOrWhiteSpace(title))
            return ServiceError.BadRequest("title is required.");

        var resolved = await preferences.ResolveForUserAsync(userId, category, ct);
        if (!resolved.InApp)
        {
            logger.LogDebug(
                "Notification suppressed for {UserId}/{Category} — in-app disabled by preferences",
                userId, category);
            return (NotificationDto?)null;
        }

        var truncatedTitle = Truncate(title, NotificationConstants.Limits.MaxTitleLength);
        var truncatedBody = body is null ? null : Truncate(body, NotificationConstants.Limits.MaxBodyLength);
        var truncatedUrl = url is null ? null : Truncate(url, NotificationConstants.Limits.MaxUrlLength);

        var entity = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Category = category,
            Title = truncatedTitle,
            Body = truncatedBody,
            Url = truncatedUrl,
            Data = data ?? new(),
            CreatedAt = DateTime.UtcNow
        };

        await repo.CreateAsync(entity, ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.NotificationsForUser(userId), ct);

        logger.LogInformation(
            "Notification created: {UserId} / {Category} / {NotificationId}",
            userId, category, entity.Id);

        return (NotificationDto?)ToDto(entity);
    }

    public async Task<ServiceResult<NotificationListResponse>> ListForCurrentUserAsync(
        bool unreadOnly, int skip, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return ServiceError.Forbidden("Authentication required.");

        var clampedTake = Math.Clamp(
            take <= 0 ? NotificationConstants.Limits.DefaultListTake : take,
            1,
            NotificationConstants.Limits.MaxListTake);
        var clampedSkip = Math.Max(0, skip);

        var items = await repo.ListAsync(currentUser.UserId, unreadOnly, clampedSkip, clampedTake, ct);
        var totalCount = await repo.CountAsync(currentUser.UserId, unreadOnly, ct);
        var unreadCount = unreadOnly
            ? totalCount
            : await repo.CountUnreadAsync(currentUser.UserId, ct);

        return new NotificationListResponse
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = totalCount,
            UnreadCount = unreadCount
        };
    }

    public async Task<ServiceResult<NotificationUnreadCountDto>> GetUnreadCountForCurrentUserAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return ServiceError.Forbidden("Authentication required.");

        var count = await cache.GetOrCreateAsync(
            CacheKeys.NotificationUnreadCount(currentUser.UserId),
            async innerCt => await repo.CountUnreadAsync(currentUser.UserId, innerCt),
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.NotificationUnreadCountTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(15)
            },
            tags: [CacheKeys.Tags.NotificationsForUser(currentUser.UserId)],
            cancellationToken: ct);

        return new NotificationUnreadCountDto { Count = count };
    }

    public async Task<ServiceResult> MarkReadAsync(Guid notificationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return ServiceError.Forbidden("Authentication required.");

        var notification = await repo.GetForOwnerAsync(notificationId, currentUser.UserId, ct);
        if (notification is null)
            return ServiceError.NotFound("Notification not found.");

        if (notification.ReadAt is null)
        {
            await repo.MarkReadAsync(notificationId, currentUser.UserId, DateTime.UtcNow, ct);
            await cache.RemoveByTagAsync(CacheKeys.Tags.NotificationsForUser(currentUser.UserId), ct);
        }

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<int>> MarkAllReadForCurrentUserAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return ServiceError.Forbidden("Authentication required.");

        var affected = await repo.MarkAllReadAsync(currentUser.UserId, DateTime.UtcNow, ct);
        if (affected > 0)
            await cache.RemoveByTagAsync(CacheKeys.Tags.NotificationsForUser(currentUser.UserId), ct);

        return affected;
    }

    public async Task<ServiceResult> DeleteAsync(Guid notificationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return ServiceError.Forbidden("Authentication required.");

        var deleted = await repo.DeleteAsync(notificationId, currentUser.UserId, ct);
        if (!deleted)
            return ServiceError.NotFound("Notification not found.");

        await cache.RemoveByTagAsync(CacheKeys.Tags.NotificationsForUser(currentUser.UserId), ct);
        return ServiceResult.Success;
    }

    private static NotificationDto ToDto(Notification n) => new()
    {
        Id = n.Id,
        Category = n.Category,
        Title = n.Title,
        Body = n.Body,
        Url = n.Url,
        Data = n.Data,
        CreatedAt = n.CreatedAt,
        ReadAt = n.ReadAt
    };

    private static string Truncate(string value, int max)
        => value.Length > max ? value[..max] : value;
}
