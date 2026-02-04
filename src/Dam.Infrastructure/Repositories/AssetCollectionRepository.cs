using Dam.Application.Repositories;
using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Dam.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for managing asset-collection relationships.
/// </summary>
public class AssetCollectionRepository : IAssetCollectionRepository
{
    private readonly AssetHubDbContext _context;

    public AssetCollectionRepository(AssetHubDbContext context)
    {
        _context = context;
    }

    public async Task<List<Collection>> GetCollectionsForAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        // Get the asset with its primary collection
        var asset = await _context.Assets
            .Include(a => a.Collection)
            .FirstOrDefaultAsync(a => a.Id == assetId, ct);

        if (asset == null)
            return new List<Collection>();

        // Get additional collections from the join table
        var additionalCollections = await _context.AssetCollections
            .Where(ac => ac.AssetId == assetId)
            .Include(ac => ac.Collection)
            .Select(ac => ac.Collection)
            .ToListAsync(ct);

        // Combine primary collection with additional collections
        var allCollections = new List<Collection> { asset.Collection };
        allCollections.AddRange(additionalCollections.Where(c => c.Id != asset.CollectionId));

        return allCollections;
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

        // Check if this is the asset's primary collection
        if (asset.CollectionId == collectionId)
            return null; // Already belongs to this collection as primary

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

        return true;
    }

    public async Task<bool> BelongsToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        // Check primary collection
        var asset = await _context.Assets.FindAsync(new object[] { assetId }, ct);
        if (asset == null)
            return false;

        if (asset.CollectionId == collectionId)
            return true;

        // Check join table
        return await _context.AssetCollections
            .AnyAsync(ac => ac.AssetId == assetId && ac.CollectionId == collectionId, ct);
    }

    public async Task<List<Guid>> GetCollectionIdsForAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        return await _context.AssetCollections
            .Where(ac => ac.AssetId == assetId)
            .Select(ac => ac.CollectionId)
            .ToListAsync(ct);
    }
}
