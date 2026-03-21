using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public class CollectionRepository(
    AssetHubDbContext dbContext,
    HybridCache cache,
    ILogger<CollectionRepository> logger) : ICollectionRepository
{
    public async Task<Collection?> GetByIdAsync(Guid id, bool includeAcls = false, CancellationToken ct = default)
    {
        var query = dbContext.Collections.AsQueryable();

        if (includeAcls)
            query = query.Include(c => c.Acls);

        return await query.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IEnumerable<Collection>> GetRootCollectionsAsync(CancellationToken ct = default)
    {
        return await dbContext.Collections
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Collection>> GetAccessibleCollectionsAsync(string userId, CancellationToken ct = default)
    {
        // Cache the accessible collection IDs per user, then load full entities
        var accessibleIds = await cache.GetOrCreateAsync(
            CacheKeys.CollectionAccess(userId),
            async cancel =>
            {
                return await dbContext.CollectionAcls
                    .Where(a => a.PrincipalId == userId && a.PrincipalType == PrincipalType.User)
                    .Select(a => a.CollectionId)
                    .Distinct()
                    .ToListAsync(cancel);
            },
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.CollectionAccessTtl,
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            [CacheKeys.Tags.CollectionAccessTag(userId), CacheKeys.Tags.CollectionAcl],
            ct);

        return await dbContext.Collections
            .Where(c => accessibleIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<Collection> CreateAsync(Collection collection, CancellationToken ct = default)
    {
        if (collection.Id == Guid.Empty)
            collection.Id = Guid.NewGuid();
        if (collection.CreatedAt == default)
            collection.CreatedAt = DateTime.UtcNow;

        dbContext.Collections.Add(collection);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Created collection {CollectionId} with name '{Name}'", collection.Id, collection.Name);
        return collection;
    }

    public async Task<Collection> UpdateAsync(Collection collection, CancellationToken ct = default)
    {
        dbContext.Collections.Update(collection);
        await dbContext.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Collection(collection.Id), ct);
        return collection;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var collection = await dbContext.Collections.FindAsync([id], ct);
        if (collection == null)
        {
            logger.LogWarning("Attempted to delete non-existent collection {CollectionId}", id);
            return;
        }

        dbContext.Collections.Remove(collection);
        await dbContext.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Collection(id), ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAcl, ct);
        logger.LogInformation("Deleted collection {CollectionId}", id);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.Collections.AnyAsync(c => c.Id == id, ct);
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        return await dbContext.Collections
            .Where(c => c.Name.ToLower() == name.ToLower())
            .Where(c => excludeId == null || c.Id != excludeId.Value)
            .AnyAsync(ct);
    }

    public async Task<Dictionary<Guid, List<string>>> GetCollectionNamesForAssetsAsync(List<Guid> assetIds, CancellationToken ct = default)
    {
        if (assetIds.Count == 0)
            return new Dictionary<Guid, List<string>>();

        // Use projection to avoid loading full Collection entities
        var rows = await dbContext.AssetCollections
            .Where(ac => assetIds.Contains(ac.AssetId))
            .Select(ac => new { ac.AssetId, CollectionName = ac.Collection.Name })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.AssetId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.CollectionName).OrderBy(n => n).ToList());
    }

    public async Task<IEnumerable<Collection>> GetAllWithAclsAsync(CancellationToken ct = default)
    {
        return await dbContext.Collections
            .Include(c => c.Acls)
            .OrderBy(c => c.Name)
            .Take(AssetHub.Application.Constants.Limits.AdminCollectionQueryLimit)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, string>> GetNamesByIdsAsync(List<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        // For small batches, cache individually per collection
        var result = new Dictionary<Guid, string>();
        var uncachedIds = new List<Guid>();

        foreach (var id in ids)
        {
            var name = await cache.GetOrCreateAsync<string?>
            (
                CacheKeys.CollectionName(id),
                cancel => default(ValueTask<string?>),
                new HybridCacheEntryOptions
                {
                    Expiration = CacheKeys.CollectionNameTtl,
                    LocalCacheExpiration = TimeSpan.FromMinutes(2)
                },
                [CacheKeys.Tags.Collection(id)],
                ct
            );

            if (name is not null)
                result[id] = name;
            else
                uncachedIds.Add(id);
        }

        if (uncachedIds.Count > 0)
        {
            var fetched = await dbContext.Collections
                .AsNoTracking()
                .Where(c => uncachedIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

            foreach (var (id, name) in fetched)
            {
                result[id] = name;
                await cache.SetAsync(
                    CacheKeys.CollectionName(id),
                    name,
                    new HybridCacheEntryOptions
                    {
                        Expiration = CacheKeys.CollectionNameTtl,
                        LocalCacheExpiration = TimeSpan.FromMinutes(2)
                    },
                    [CacheKeys.Tags.Collection(id)],
                    ct);
            }
        }

        return result;
    }

    public async Task<Dictionary<Guid, int>> GetAssetCountsAsync(IEnumerable<Guid> collectionIds, CancellationToken ct = default)
    {
        var idList = collectionIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<Guid, int>();

        var result = new Dictionary<Guid, int>();
        var uncachedIds = new List<Guid>();

        foreach (var id in idList)
        {
            var count = await cache.GetOrCreateAsync<int?>
            (
                CacheKeys.CollectionCount(id),
                cancel => default(ValueTask<int?>),
                new HybridCacheEntryOptions
                {
                    Expiration = CacheKeys.CollectionCountTtl,
                    LocalCacheExpiration = TimeSpan.FromSeconds(30)
                },
                [CacheKeys.Tags.Collection(id)],
                ct
            );

            if (count.HasValue)
                result[id] = count.Value;
            else
                uncachedIds.Add(id);
        }

        if (uncachedIds.Count > 0)
        {
            var fetched = await dbContext.AssetCollections
                .Where(ac => uncachedIds.Contains(ac.CollectionId))
                .GroupBy(ac => ac.CollectionId)
                .Select(g => new { CollectionId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CollectionId, x => x.Count, ct);

            foreach (var id in uncachedIds)
            {
                var count = fetched.GetValueOrDefault(id);
                result[id] = count;
                await cache.SetAsync(
                    CacheKeys.CollectionCount(id),
                    (int?)count,
                    new HybridCacheEntryOptions
                    {
                        Expiration = CacheKeys.CollectionCountTtl,
                        LocalCacheExpiration = TimeSpan.FromSeconds(30)
                    },
                    [CacheKeys.Tags.Collection(id)],
                    ct);
            }
        }

        return result;
    }

}
