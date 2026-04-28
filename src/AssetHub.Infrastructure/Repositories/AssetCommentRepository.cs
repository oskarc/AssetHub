using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class AssetCommentRepository(DbContextProvider provider) : IAssetCommentRepository
{
    public async Task<List<AssetComment>> ListByAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.AssetComments
            .AsNoTracking()
            .Where(c => c.AssetId == assetId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<AssetComment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.AssetComments
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<AssetComment> CreateAsync(AssetComment comment, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        db.AssetComments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment;
    }

    public async Task UpdateAsync(AssetComment comment, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        db.AssetComments.Update(comment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var rows = await db.AssetComments
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }
}
