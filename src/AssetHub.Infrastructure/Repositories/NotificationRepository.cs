using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class NotificationRepository(AssetHubDbContext db) : INotificationRepository
{
    public async Task<Notification> CreateAsync(Notification notification, CancellationToken ct = default)
    {
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
        return notification;
    }

    public async Task<List<Notification>> ListAsync(
        string userId, bool unreadOnly, int skip, int take, CancellationToken ct = default)
    {
        var query = db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (unreadOnly)
            query = query.Where(n => n.ReadAt == null);

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(string userId, bool unreadOnly, CancellationToken ct = default)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
            query = query.Where(n => n.ReadAt == null);
        return await query.CountAsync(ct);
    }

    public async Task<int> CountUnreadAsync(string userId, CancellationToken ct = default)
    {
        return await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .CountAsync(ct);
    }

    public async Task<Notification?> GetForOwnerAsync(Guid id, string userId, CancellationToken ct = default)
    {
        return await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
    }

    public async Task<bool> MarkReadAsync(Guid id, string userId, DateTime readAt, CancellationToken ct = default)
    {
        var rows = await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, readAt), ct);
        return rows > 0;
    }

    public async Task<int> MarkAllReadAsync(string userId, DateTime readAt, CancellationToken ct = default)
    {
        return await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, readAt), ct);
    }

    public async Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default)
    {
        var rows = await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }
}
