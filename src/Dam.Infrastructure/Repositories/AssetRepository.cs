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
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<List<Asset>> GetByCollectionAsync(Guid collectionId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Assets
            .Where(a => dbContext.AssetCollections
                .Where(ac => ac.CollectionId == collectionId)
                .Select(ac => ac.AssetId)
                .Contains(a.Id))
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
        return await dbContext.AssetCollections
            .Where(ac => ac.CollectionId == collectionId)
            .CountAsync(cancellationToken);
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

    public async Task<List<Asset>> DeleteByCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        // Find all assets linked to this collection
        var assetIds = await dbContext.AssetCollections
            .Where(ac => ac.CollectionId == collectionId)
            .Select(ac => ac.AssetId)
            .ToListAsync(cancellationToken);

        if (assetIds.Count == 0) return new List<Asset>();

        // For each asset, determine whether it belongs to OTHER collections too
        var sharedAssetIds = await dbContext.AssetCollections
            .Where(ac => assetIds.Contains(ac.AssetId) && ac.CollectionId != collectionId)
            .Select(ac => ac.AssetId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var exclusiveAssetIds = assetIds.Except(sharedAssetIds).ToList();

        // Remove all links for this collection (covers both shared and exclusive)
        var links = await dbContext.AssetCollections
            .Where(ac => ac.CollectionId == collectionId)
            .ToListAsync(cancellationToken);
        dbContext.AssetCollections.RemoveRange(links);

        // Hard-delete assets that were exclusive to this collection
        var exclusiveAssets = new List<Asset>();
        if (exclusiveAssetIds.Count > 0)
        {
            exclusiveAssets = await dbContext.Assets
                .Where(a => exclusiveAssetIds.Contains(a.Id))
                .ToListAsync(cancellationToken);
            dbContext.Assets.RemoveRange(exclusiveAssets);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return exclusiveAssets; // caller can clean up MinIO for these
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
        var collectionAssetIds = dbContext.AssetCollections
            .Where(ac => ac.CollectionId == collectionId)
            .Select(ac => ac.AssetId);

        var queryable = dbContext.Assets
            .Where(a => collectionAssetIds.Contains(a.Id));

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
        string? query = null,
        string? assetType = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50,
        List<Guid>? allowedCollectionIds = null,
        CancellationToken cancellationToken = default)
    {
        var queryable = dbContext.Assets
            .Where(a => a.Status == Asset.StatusReady);

        // Filter to assets in allowed collections (ACL enforcement)
        if (allowedCollectionIds != null)
        {
            var allowedAssetIds = dbContext.AssetCollections
                .Where(ac => allowedCollectionIds.Contains(ac.CollectionId))
                .Select(ac => ac.AssetId);
            queryable = queryable.Where(a => allowedAssetIds.Contains(a.Id));
        }

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
