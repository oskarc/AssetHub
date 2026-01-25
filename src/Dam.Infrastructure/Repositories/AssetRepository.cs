using Dam.Application.Repositories;
using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Dam.Infrastructure.Repositories;

public class AssetRepository(AssetHubDbContext dbContext) : IAssetRepository
{
    public async Task<Asset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .Include(a => a.Collection)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<List<Asset>> GetByCollectionAsync(Guid collectionId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .Where(a => a.CollectionId == collectionId)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Asset>> GetByTypeAsync(string assetType, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .Where(a => a.AssetType == assetType)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Asset>> GetByStatusAsync(string status, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .Where(a => a.Status == status)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Asset>> GetByUserAsync(string userId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .Where(a => a.CreatedByUserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountByCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .CountAsync(a => a.CollectionId == collectionId, cancellationToken);
    }

    public async Task<int> CountByStatusAsync(string status, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .CountAsync(a => a.Status == status, cancellationToken);
    }

    public async Task<Asset?> GetByOriginalKeyAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .FirstOrDefaultAsync(a => a.OriginalObjectKey == objectKey, cancellationToken);
    }

    public async Task CreateAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        dbContext.Assets.Add(asset);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        dbContext.Assets.Update(asset);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await dbContext.Assets.FindAsync(new object[] { id }, cancellationToken);
        if (asset != null)
        {
            dbContext.Assets.Remove(asset);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var assets = await dbContext.Assets
            .Where(a => a.CollectionId == collectionId)
            .ToListAsync(cancellationToken);
        
        dbContext.Assets.RemoveRange(assets);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
