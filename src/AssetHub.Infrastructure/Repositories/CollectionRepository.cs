using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public class CollectionRepository(AssetHubDbContext dbContext) : ICollectionRepository
{
    public async Task<Collection?> GetByIdAsync(Guid id, bool includeAcls = false, bool includeChildren = false, CancellationToken ct = default)
    {
        var query = dbContext.Collections.AsQueryable();

        if (includeAcls)
            query = query.Include(c => c.Acls);

        if (includeChildren)
            query = query.Include(c => c.Children);

        return await query.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IEnumerable<Collection>> GetRootCollectionsAsync(CancellationToken ct = default)
    {
        return await dbContext.Collections
            .Where(c => c.ParentId == null)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Collection>> GetChildrenAsync(Guid parentId, CancellationToken ct = default)
    {
        return await dbContext.Collections
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Collection>> GetAccessibleCollectionsAsync(string userId, CancellationToken ct = default)
    {
        // Use a recursive CTE to find all collections accessible via direct ACL
        // or inherited from a parent collection's ACL.
        var accessibleIds = await dbContext.Database
            .SqlQueryRaw<Guid>(@"
                WITH RECURSIVE accessible AS (
                    -- Base case: collections where the user has a direct ACL
                    SELECT c.""Id"", c.""ParentId""
                    FROM ""Collections"" c
                    INNER JOIN ""CollectionAcls"" a ON a.""CollectionId"" = c.""Id""
                    WHERE a.""PrincipalId"" = {0} AND a.""PrincipalType"" = 'user'
                    UNION
                    -- Recursive case: children of accessible collections
                    SELECT child.""Id"", child.""ParentId""
                    FROM ""Collections"" child
                    INNER JOIN accessible acc ON child.""ParentId"" = acc.""Id""
                )
                SELECT DISTINCT ""Id"" AS ""Value"" FROM accessible
            ", userId)
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
        // Leverage the DB's ON DELETE CASCADE for the parent-child relationship.
        // Only load the root entity — PostgreSQL cascades to all descendants.
        var collection = await dbContext.Collections.FindAsync([id], ct);
        if (collection == null) return;

        dbContext.Collections.Remove(collection);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.Collections.AnyAsync(c => c.Id == id, ct);
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        return await dbContext.Collections
            .Where(c => c.ParentId == null)
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
