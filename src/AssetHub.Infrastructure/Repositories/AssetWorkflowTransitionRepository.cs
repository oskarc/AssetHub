using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class AssetWorkflowTransitionRepository(DbContextProvider provider)
    : IAssetWorkflowTransitionRepository
{
    public async Task<List<AssetWorkflowTransition>> ListByAssetAsync(
        Guid assetId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.AssetWorkflowTransitions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<AssetWorkflowTransition> CreateAsync(
        AssetWorkflowTransition entity, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        db.AssetWorkflowTransitions.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }
}
