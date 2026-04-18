using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public class ShareRepository(
    AssetHubDbContext dbContext,
    ILogger<ShareRepository> logger) : IShareRepository
{
    public async Task<Share?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Share?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<List<Share>> GetByScopeAsync(string scopeType, Guid scopeId, CancellationToken cancellationToken = default)
    {
        var scope = Enum.Parse<ShareScopeType>(scopeType, true);
        return await dbContext.Shares
            .AsNoTracking()
            .Where(s => s.ScopeType == scope && s.ScopeId == scopeId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Share>> GetByUserAsync(string userId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .AsNoTracking()
            .Where(s => s.CreatedByUserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares.CountAsync(cancellationToken);
    }

    public async Task<List<Share>> GetAllAsync(ShareQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ShareQueryOptions();

        var shares = await dbContext.Shares
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Skip(options.Skip)
            .Take(options.Take)
            .ToListAsync(cancellationToken);

        // Asset and Collection are polymorphic (via ScopeType/ScopeId), not FK relationships
        // so we need to manually load them
        if (options.IncludeAsset)
            await LoadAssetsAsync(shares, cancellationToken);

        if (options.IncludeCollection)
            await LoadCollectionsAsync(shares, cancellationToken);

        return shares;
    }

    private async Task LoadAssetsAsync(List<Share> shares, CancellationToken cancellationToken)
    {
        var assetShares = shares.Where(s => s.ScopeType == ShareScopeType.Asset).ToList();
        if (assetShares.Count == 0)
            return;

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

    private async Task LoadCollectionsAsync(List<Share> shares, CancellationToken cancellationToken)
    {
        var collectionShares = shares.Where(s => s.ScopeType == ShareScopeType.Collection).ToList();
        if (collectionShares.Count == 0)
            return;

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

    public async Task CreateAsync(Share share, CancellationToken cancellationToken = default)
    {
        dbContext.Shares.Add(share);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created share {ShareId} for {ScopeType} {ScopeId}", share.Id, share.ScopeType, share.ScopeId);
    }

    public async Task UpdateAsync(Share share, CancellationToken cancellationToken = default)
    {
        dbContext.Shares.Update(share);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var share = await dbContext.Shares.FindAsync(new object[] { id }, cancellationToken);
        if (share is not null)
        {
            dbContext.Shares.Remove(share);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Deleted share {ShareId}", id);
        }
        else
        {
            logger.LogWarning("Attempted to delete non-existent share {ShareId}", id);
        }
    }

    public async Task DeleteByScopeAsync(string scopeType, Guid scopeId, CancellationToken cancellationToken = default)
    {
        var scope = Enum.Parse<ShareScopeType>(scopeType, true);
        await dbContext.Shares
            .Where(s => s.ScopeType == scope && s.ScopeId == scopeId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteByScopeBatchAsync(string scopeType, IEnumerable<Guid> scopeIds, CancellationToken cancellationToken = default)
    {
        var ids = scopeIds.ToList();
        if (ids.Count == 0) return;

        var scope = Enum.Parse<ShareScopeType>(scopeType, true);
        await dbContext.Shares
            .Where(s => s.ScopeType == scope && ids.Contains(s.ScopeId))
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

    public async Task<int> DeleteOrphanedAsync(CancellationToken cancellationToken = default)
    {
        // Find shares pointing to non-existent assets
        var orphanedAssetShares = await dbContext.Shares
            .Where(s => s.ScopeType == ShareScopeType.Asset)
            .Where(s => !dbContext.Assets.Any(a => a.Id == s.ScopeId))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        // Find shares pointing to non-existent collections
        var orphanedCollectionShares = await dbContext.Shares
            .Where(s => s.ScopeType == ShareScopeType.Collection)
            .Where(s => !dbContext.Collections.Any(c => c.Id == s.ScopeId))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var orphanedIds = orphanedAssetShares.Concat(orphanedCollectionShares).ToList();

        if (orphanedIds.Count == 0)
            return 0;

        var deleted = await dbContext.Shares
            .Where(s => orphanedIds.Contains(s.Id))
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation(
            "Deleted {Deleted} orphaned shares ({AssetShareCount} asset, {CollectionShareCount} collection)",
            deleted, orphanedAssetShares.Count, orphanedCollectionShares.Count);

        return deleted;
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await dbContext.Shares
            .Where(s => s.RevokedAt == null && s.ExpiresAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Deleted {Deleted} expired shares", deleted);
        return deleted;
    }

    public async Task<int> DeleteRevokedAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await dbContext.Shares
            .Where(s => s.RevokedAt != null)
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation("Deleted {Deleted} revoked shares", deleted);
        return deleted;
    }
}
