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
        // Get all collections from the join table only
        var collections = await _context.AssetCollections
            .Where(ac => ac.AssetId == assetId)
            .Include(ac => ac.Collection)
            .Select(ac => ac.Collection)
            .ToListAsync(ct);

        return collections;
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
        // Check if it's in the collections join table
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
