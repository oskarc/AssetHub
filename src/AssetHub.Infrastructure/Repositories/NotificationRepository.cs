using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class NotificationRepository(DbContextProvider provider) : INotificationRepository
{
    public async Task<Notification> CreateAsync(Notification notification, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
        return notification;
    }

    public async Task<List<Notification>> ListAsync(
        string userId, bool unreadOnly, int skip, int take, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var query = lease.Db.Notifications
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
        await using var lease = await provider.AcquireAsync(ct);
        var query = lease.Db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
            query = query.Where(n => n.ReadAt == null);
        return await query.CountAsync(ct);
    }

    public async Task<int> CountUnreadAsync(string userId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .CountAsync(ct);
    }

    public async Task<Notification?> GetForOwnerAsync(Guid id, string userId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
    }

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, ct);
    }

    public async Task<bool> MarkReadAsync(Guid id, string userId, DateTime readAt, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var rows = await lease.Db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, readAt), ct);
        return rows > 0;
    }

    public async Task<int> MarkAllReadAsync(string userId, DateTime readAt, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, readAt), ct);
    }

    public async Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var rows = await lease.Db.Notifications
            .Where(n => n.Id == id && n.UserId == userId)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }
}
