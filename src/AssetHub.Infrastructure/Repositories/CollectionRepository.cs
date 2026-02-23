using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public class CollectionRepository(
    AssetHubDbContext dbContext,
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
        // Find all collections where the user has a direct ACL entry
        var accessibleIds = await dbContext.CollectionAcls
            .Where(a => a.PrincipalId == userId && a.PrincipalType == PrincipalType.User)
            .Select(a => a.CollectionId)
            .Distinct()
            .ToListAsync(ct);

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

}
