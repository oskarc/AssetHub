using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AssetHub.Infrastructure.Repositories;

public class AssetRepository(AssetHubDbContext dbContext, IMemoryCache cache) : IAssetRepository
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
        if (!Enum.TryParse<AssetType>(assetType, ignoreCase: true, out var type))
            return new List<Asset>();
        return await dbContext.Assets
            .Where(a => a.AssetType == type)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Asset>> GetByStatusAsync(string status, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<AssetStatus>(status, ignoreCase: true, out var statusEnum))
            return new List<Asset>();
        return await dbContext.Assets
            .Where(a => a.Status == statusEnum)
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
        if (!Enum.TryParse<AssetStatus>(status, ignoreCase: true, out var statusEnum))
            return 0;
        return await dbContext.Assets
            .CountAsync(a => a.Status == statusEnum, cancellationToken);
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
        // Use REPEATABLE READ isolation so that the read-decide-delete logic is
        // consistent: no other transaction can add an "exclusive" asset to a second
        // collection between our membership check and the DELETE.
        return await dbContext.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                await using var tx = await dbContext.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.RepeatableRead, cancellationToken);

                // Find all assets linked to this collection
                var assetIds = await dbContext.AssetCollections
                    .Where(ac => ac.CollectionId == collectionId)
                    .Select(ac => ac.AssetId)
                    .ToListAsync(cancellationToken);

                if (assetIds.Count == 0)
                {
                    await tx.CommitAsync(cancellationToken);
                    return new List<Asset>();
                }

                // Determine which assets belong to OTHER collections too
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
                await tx.CommitAsync(cancellationToken);

                // Invalidate cached collection IDs for all affected assets
                foreach (var assetId in assetIds)
                    CacheKeys.InvalidateAssetCollectionIds(cache, assetId);

                return exclusiveAssets; // caller can clean up MinIO for these
            });
    }

    public async Task<(List<Asset> Assets, int Total)> SearchAsync(
        Guid collectionId,
        string? query = null,
        string? assetType = null,
        string sortBy = Constants.SortBy.CreatedDesc,
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
            // Search is limited to title and description. Tag search could use PostgreSQL
            // JSONB operators but would require different query construction per filter.
        }

        // Apply asset type filter
        if (!string.IsNullOrWhiteSpace(assetType))
        {
            if (!Enum.TryParse<AssetType>(assetType, ignoreCase: true, out var type))
                return (new List<Asset>(), 0);
            queryable = queryable.Where(a => a.AssetType == type);
        }

        // Get total count before pagination
        var total = await queryable.CountAsync(cancellationToken);

        // Apply sorting and pagination
        var assets = await ApplySorting(queryable, sortBy)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (assets, total);
    }

    public async Task<(List<Asset> Assets, int Total)> SearchAllAsync(
        string? query = null,
        string? assetType = null,
        string sortBy = Constants.SortBy.CreatedDesc,
        int skip = 0,
        int take = 50,
        List<Guid>? allowedCollectionIds = null,
        bool includeAllStatuses = false,
        CancellationToken cancellationToken = default)
    {
        var queryable = includeAllStatuses
            ? dbContext.Assets.Where(a => a.Status != AssetStatus.Uploading)
            : dbContext.Assets.Where(a => a.Status == AssetStatus.Ready);

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
            if (!Enum.TryParse<AssetType>(assetType, ignoreCase: true, out var type))
                return (new List<Asset>(), 0);
            queryable = queryable.Where(a => a.AssetType == type);
        }

        // Get total count before pagination
        var total = await queryable.CountAsync(cancellationToken);

        // Apply sorting and pagination
        var assets = await ApplySorting(queryable, sortBy)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (assets, total);
    }

    /// <summary>
    /// Applies a sort expression to an asset query. Shared by SearchAsync and SearchAllAsync.
    /// </summary>
    private static IQueryable<Asset> ApplySorting(IQueryable<Asset> queryable, string sortBy) =>
        sortBy switch
        {
            Constants.SortBy.CreatedAsc => queryable.OrderBy(a => a.CreatedAt),
            Constants.SortBy.CreatedDesc => queryable.OrderByDescending(a => a.CreatedAt),
            Constants.SortBy.TitleAsc => queryable.OrderBy(a => a.Title),
            Constants.SortBy.TitleDesc => queryable.OrderByDescending(a => a.Title),
            Constants.SortBy.SizeAsc => queryable.OrderBy(a => a.SizeBytes),
            Constants.SortBy.SizeDesc => queryable.OrderByDescending(a => a.SizeBytes),
            _ => queryable.OrderByDescending(a => a.CreatedAt)
        };
}
