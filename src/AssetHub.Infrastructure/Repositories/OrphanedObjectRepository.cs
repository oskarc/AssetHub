using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class OrphanedObjectRepository(DbContextProvider provider) : IOrphanedObjectRepository
{
    private const int MaxErrorLength = 1000;

    public async Task EnqueueAsync(OrphanedObject obj, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        if (obj.Id == Guid.Empty) obj.Id = Guid.NewGuid();
        if (obj.CreatedAt == default) obj.CreatedAt = DateTime.UtcNow;
        db.OrphanedObjects.Add(obj);
        await db.SaveChangesAsync(ct);
    }

    public async Task EnqueueBatchAsync(IEnumerable<OrphanedObject> objs, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var now = DateTime.UtcNow;
        var toAdd = objs
            .Where(o => !string.IsNullOrEmpty(o.ObjectKey))
            .Select(o =>
            {
                if (o.Id == Guid.Empty) o.Id = Guid.NewGuid();
                if (o.CreatedAt == default) o.CreatedAt = now;
                return o;
            })
            .ToList();
        if (toAdd.Count == 0) return;
        db.OrphanedObjects.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<OrphanedObject>> GetNextBatchAsync(int take, int maxAttempts, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.OrphanedObjects
            .Where(o => o.AttemptCount < maxAttempts)
            .OrderBy(o => o.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        await db.OrphanedObjects
            .Where(o => o.Id == id)
            .ExecuteDeleteAsync(ct);
    }

    public async Task RecordFailureAsync(Guid id, string error, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var truncated = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        var now = DateTime.UtcNow;
        await db.OrphanedObjects
            .Where(o => o.Id == id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(o => o.AttemptCount, o => o.AttemptCount + 1)
                .SetProperty(o => o.LastAttemptAt, now)
                .SetProperty(o => o.LastError, truncated), ct);
    }
}
