using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for managing asset-collection relationships.
/// Caches GetCollectionIdsForAssetAsync (the primary hot path used in authorization checks).
/// </summary>
public class AssetCollectionRepository : IAssetCollectionRepository
{
    private readonly AssetHubDbContext _context;
    private readonly HybridCache _cache;
    private readonly ILogger<AssetCollectionRepository> _logger;

    public AssetCollectionRepository(AssetHubDbContext context, HybridCache cache, ILogger<AssetCollectionRepository> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<Collection>> GetCollectionsForAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        return await _context.AssetCollections
            .AsNoTracking()
            .Where(ac => ac.AssetId == assetId)
            .Include(ac => ac.Collection)
            .Select(ac => ac.Collection)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, List<Guid>>> GetCollectionIdsForAssetsAsync(IEnumerable<Guid> assetIds, CancellationToken ct = default)
    {
        var ids = assetIds.ToList();
        var mappings = await _context.AssetCollections
            .Where(ac => ids.Contains(ac.AssetId))
            .Select(ac => new { ac.AssetId, ac.CollectionId })
            .ToListAsync(ct);

        return mappings
            .GroupBy(m => m.AssetId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => m.CollectionId).ToList());
    }

    public async Task<List<AssetCollection>> GetByAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        return await _context.AssetCollections
            .Where(ac => ac.AssetId == assetId)
            .Include(ac => ac.Collection)
            .ToListAsync(ct);
    }

    public async Task<List<AssetCollection>> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        return await _context.AssetCollections
            .AsNoTracking()
            .Where(ac => ac.CollectionId == collectionId)
            .Include(ac => ac.Asset)
            .ToListAsync(ct);
    }

    public async Task<AssetCollection?> AddToCollectionAsync(Guid assetId, Guid collectionId, string userId, CancellationToken ct = default)
    {
        // Check if asset exists
        var asset = await _context.Assets.FindAsync(new object[] { assetId }, ct);
        if (asset == null)
            return null;

        // Check if collection exists
        var collection = await _context.Collections.FindAsync(new object[] { collectionId }, ct);
        if (collection == null)
            return null;

        // Check if already linked
        var existing = await _context.AssetCollections
            .FirstOrDefaultAsync(ac => ac.AssetId == assetId && ac.CollectionId == collectionId, ct);

        if (existing != null)
            return null; // Already linked

        var assetCollection = new AssetCollection
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            CollectionId = collectionId,
            AddedAt = DateTime.UtcNow,
            AddedByUserId = userId
        };

        _context.AssetCollections.Add(assetCollection);
        await _context.SaveChangesAsync(ct);

        // Invalidate cached collection IDs for this asset
        await _cache.RemoveByTagAsync(CacheKeys.Tags.AssetCollections(assetId), ct);
        await _cache.RemoveByTagAsync(CacheKeys.Tags.Collection(collectionId), ct);
        _logger.LogDebug("Cache invalidated: asset-collection IDs for asset {AssetId}", assetId);

        return assetCollection;
    }

    public async Task<bool> RemoveFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var assetCollection = await _context.AssetCollections
            .FirstOrDefaultAsync(ac => ac.AssetId == assetId && ac.CollectionId == collectionId, ct);

        if (assetCollection == null)
            return false;

        _context.AssetCollections.Remove(assetCollection);
        await _context.SaveChangesAsync(ct);

        // Invalidate cached collection IDs for this asset
        await _cache.RemoveByTagAsync(CacheKeys.Tags.AssetCollections(assetId), ct);
        await _cache.RemoveByTagAsync(CacheKeys.Tags.Collection(collectionId), ct);
        _logger.LogDebug("Cache invalidated: asset-collection IDs for asset {AssetId}", assetId);

        return true;
    }

    public async Task<bool> BelongsToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        // Check if it's in the collections join table
        return await _context.AssetCollections
            .AnyAsync(ac => ac.AssetId == assetId && ac.CollectionId == collectionId, ct);
    }

    public async Task UnlinkAllFromCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        var links = await _context.AssetCollections
            .Where(ac => ac.CollectionId == collectionId)
            .ToListAsync(ct);

        if (links.Count == 0) return;

        var assetIds = links.Select(l => l.AssetId).ToList();
        _context.AssetCollections.RemoveRange(links);
        await _context.SaveChangesAsync(ct);

        var cacheTasks = assetIds.Select(id => _cache.RemoveByTagAsync(CacheKeys.Tags.AssetCollections(id), ct).AsTask());
        await Task.WhenAll(cacheTasks);
        await _cache.RemoveByTagAsync(CacheKeys.Tags.Collection(collectionId), ct);
    }

    public async Task<List<Guid>> GetCollectionIdsForAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.AssetCollectionIds(assetId);
        var tags = new[] { CacheKeys.Tags.AssetCollections(assetId) };

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel =>
            {
                var result = await _context.AssetCollections
                    .Where(ac => ac.AssetId == assetId)
                    .Select(ac => ac.CollectionId)
                    .ToListAsync(cancel);
                return result;
            },
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.AssetCollectionIdsTtl,
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            tags,
            ct);
    }
}
