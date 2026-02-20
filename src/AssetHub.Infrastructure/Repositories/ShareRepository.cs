using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public class ShareRepository(AssetHubDbContext dbContext) : IShareRepository
{
    public async Task<Share?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Share?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<List<Share>> GetByScopeAsync(string scopeType, Guid scopeId, CancellationToken cancellationToken = default)
    {
        var scope = Enum.Parse<ShareScopeType>(scopeType, true);
        return await dbContext.Shares
            .Where(s => s.ScopeType == scope && s.ScopeId == scopeId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Share>> GetByUserAsync(string userId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .Where(s => s.CreatedByUserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Share>> GetAllAsync(bool includeAsset = false, bool includeCollection = false, CancellationToken cancellationToken = default)
    {
        var shares = await dbContext.Shares
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        // Asset and Collection are polymorphic (via ScopeType/ScopeId), not FK relationships
        // so we need to manually load them
        if (includeAsset || includeCollection)
        {
            var assetShares = shares.Where(s => s.ScopeType == ShareScopeType.Asset).ToList();
            var collectionShares = shares.Where(s => s.ScopeType == ShareScopeType.Collection).ToList();

            if (includeAsset && assetShares.Count > 0)
            {
                var assetIds = assetShares.Select(s => s.ScopeId).Distinct().ToList();
                var assets = await dbContext.Assets
                    .Where(a => assetIds.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, cancellationToken);

                foreach (var share in assetShares)
                {
                    if (assets.TryGetValue(share.ScopeId, out var asset))
                        share.Asset = asset;
                }
            }

            if (includeCollection && collectionShares.Count > 0)
            {
                var collectionIds = collectionShares.Select(s => s.ScopeId).Distinct().ToList();
                var collections = await dbContext.Collections
                    .Where(c => collectionIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, cancellationToken);

                foreach (var share in collectionShares)
                {
                    if (collections.TryGetValue(share.ScopeId, out var collection))
                        share.Collection = collection;
                }
            }
        }

        return shares;
    }

    public async Task CreateAsync(Share share, CancellationToken cancellationToken = default)
    {
        dbContext.Shares.Add(share);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Share share, CancellationToken cancellationToken = default)
    {
        dbContext.Shares.Update(share);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var share = await dbContext.Shares.FindAsync(new object[] { id }, cancellationToken);
        if (share != null)
        {
            dbContext.Shares.Remove(share);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByScopeAsync(string scopeType, Guid scopeId, CancellationToken cancellationToken = default)
    {
        var scope = Enum.Parse<ShareScopeType>(scopeType, true);
        await dbContext.Shares
            .Where(s => s.ScopeType == scope && s.ScopeId == scopeId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task IncrementAccessAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await dbContext.Shares
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.AccessCount, p => p.AccessCount + 1)
                .SetProperty(p => p.LastAccessedAt, DateTime.UtcNow),
                cancellationToken);
    }
}
