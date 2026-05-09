using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

/// <summary>
/// CRUD for queued analytics PDF export jobs (T5-ANL-01). Same shape as
/// <c>WebhookDeliveryRepository</c> — small surface, no caching.
/// </summary>
public sealed class AnalyticsPdfJobRepository(DbContextProvider provider) : IAnalyticsPdfJobRepository
{
    public async Task<AnalyticsPdfJob?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.AnalyticsPdfJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public async Task AddAsync(AnalyticsPdfJob job, CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        if (job.Id == Guid.Empty) job.Id = Guid.NewGuid();
        if (job.CreatedAt == default) job.CreatedAt = DateTime.UtcNow;
        db.AnalyticsPdfJobs.Add(job);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AnalyticsPdfJob job, CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        db.AnalyticsPdfJobs.Update(job);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteExpiredAsync(DateTime now, CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.AnalyticsPdfJobs
            .Where(j => j.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);
    }
}
