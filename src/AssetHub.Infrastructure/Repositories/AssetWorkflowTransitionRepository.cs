using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class AssetWorkflowTransitionRepository(AssetHubDbContext db)
    : IAssetWorkflowTransitionRepository
{
    public async Task<List<AssetWorkflowTransition>> ListByAssetAsync(
        Guid assetId, CancellationToken ct = default)
    {
        return await db.AssetWorkflowTransitions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<AssetWorkflowTransition> CreateAsync(
        AssetWorkflowTransition entity, CancellationToken ct = default)
    {
        db.AssetWorkflowTransitions.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }
}
