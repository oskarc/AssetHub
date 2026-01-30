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

    public async Task<(List<Asset> Assets, int Total)> SearchAsync(
        Guid collectionId,
        string? query = null,
        string? assetType = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var queryable = dbContext.Assets
            .Where(a => a.CollectionId == collectionId);

        // Apply text search filter
        if (!string.IsNullOrWhiteSpace(query))
        {
            var searchPattern = $"%{query}%";
            queryable = queryable.Where(a =>
                EF.Functions.ILike(a.Title, searchPattern) ||
                (a.Description != null && EF.Functions.ILike(a.Description, searchPattern)));
            // Note: Tag search requires client-side evaluation for complex patterns
            // For now, we search only in title and description which covers most use cases
        }

        // Apply asset type filter
        if (!string.IsNullOrWhiteSpace(assetType))
        {
            queryable = queryable.Where(a => a.AssetType == assetType);
        }

        // Get total count before pagination
        var total = await queryable.CountAsync(cancellationToken);

        // Apply sorting
        queryable = sortBy switch
        {
            "created_asc" => queryable.OrderBy(a => a.CreatedAt),
            "created_desc" => queryable.OrderByDescending(a => a.CreatedAt),
            "title_asc" => queryable.OrderBy(a => a.Title),
            "title_desc" => queryable.OrderByDescending(a => a.Title),
            "size_asc" => queryable.OrderBy(a => a.SizeBytes),
            "size_desc" => queryable.OrderByDescending(a => a.SizeBytes),
            _ => queryable.OrderByDescending(a => a.CreatedAt)
        };

        // Apply pagination
        var assets = await queryable
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (assets, total);
    }

    public async Task<(List<Asset> Assets, int Total)> SearchAllAsync(
        IEnumerable<Guid>? accessibleCollectionIds,
        string? query = null,
        string? assetType = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        // If no accessible collections, return empty
        var collectionIdList = accessibleCollectionIds?.ToList();
        if (collectionIdList == null || collectionIdList.Count == 0)
        {
            return (new List<Asset>(), 0);
        }

        var queryable = dbContext.Assets
            .Include(a => a.Collection)
            .Where(a => a.Status == Asset.StatusReady)
            .Where(a => collectionIdList.Contains(a.CollectionId));

        // Apply text search filter
        if (!string.IsNullOrWhiteSpace(query))
        {
            var searchPattern = $"%{query}%";
            queryable = queryable.Where(a =>
                EF.Functions.ILike(a.Title, searchPattern) ||
                (a.Description != null && EF.Functions.ILike(a.Description, searchPattern)));
        }

        // Apply asset type filter
        if (!string.IsNullOrWhiteSpace(assetType))
        {
            queryable = queryable.Where(a => a.AssetType == assetType);
        }

        // Get total count before pagination
        var total = await queryable.CountAsync(cancellationToken);

        // Apply sorting
        queryable = sortBy switch
        {
            "created_asc" => queryable.OrderBy(a => a.CreatedAt),
            "created_desc" => queryable.OrderByDescending(a => a.CreatedAt),
            "title_asc" => queryable.OrderBy(a => a.Title),
            "title_desc" => queryable.OrderByDescending(a => a.Title),
            "size_asc" => queryable.OrderBy(a => a.SizeBytes),
            "size_desc" => queryable.OrderByDescending(a => a.SizeBytes),
            _ => queryable.OrderByDescending(a => a.CreatedAt)
        };

        // Apply pagination
        var assets = await queryable
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (assets, total);
    }
}
