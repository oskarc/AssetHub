namespace Dam.Infrastructure.Repositories;

using Dam.Application.Repositories;
using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
        return await dbContext.Collections
            .Where(c => c.Acls.Any(a =>
                a.PrincipalType == "user" &&
                a.PrincipalId == userId))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<Collection> CreateAsync(Collection collection, CancellationToken ct = default)
    {
        if (collection.Id == Guid.Empty)
            collection.Id = Guid.NewGuid();
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
        var collection = await GetByIdAsync(id, includeChildren: true, ct: ct);
        if (collection == null) return;

        await DeleteRecursiveAsync(collection, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.Collections.AnyAsync(c => c.Id == id, ct);
    }

    public async Task<IEnumerable<Collection>> GetAllWithAclsAsync(CancellationToken ct = default)
    {
        return await dbContext.Collections
            .Include(c => c.Acls)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    private async Task DeleteRecursiveAsync(Collection collection, CancellationToken ct = default)
    {
        var children = await dbContext.Collections
            .Where(c => c.ParentId == collection.Id)
            .ToListAsync(ct);

        foreach (var child in children)
        {
            await DeleteRecursiveAsync(child, ct);
        }

        dbContext.Collections.Remove(collection);
    }
}

public class CollectionAclRepository(AssetHubDbContext dbContext) : ICollectionAclRepository
{
    public async Task<IEnumerable<CollectionAcl>> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        return await dbContext.CollectionAcls
            .Where(a => a.CollectionId == collectionId)
            .OrderBy(a => a.PrincipalType)
            .ThenBy(a => a.PrincipalId)
            .ToListAsync(ct);
    }

    public async Task<CollectionAcl?> GetByPrincipalAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default)
    {
        return await dbContext.CollectionAcls
            .FirstOrDefaultAsync(a =>
                a.CollectionId == collectionId &&
                a.PrincipalType == principalType &&
                a.PrincipalId == principalId, ct);
    }
    
    public async Task<IEnumerable<CollectionAcl>> GetByUserAsync(string userId, CancellationToken ct = default)
    {
        return await dbContext.CollectionAcls
            .Where(a => a.PrincipalType == "user" && a.PrincipalId == userId)
            .ToListAsync(ct);
    }

    public async Task<CollectionAcl> SetAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default)
    {
        var existing = await GetByPrincipalAsync(collectionId, principalType, principalId, ct);

        if (existing != null)
        {
            existing.Role = role;
            dbContext.CollectionAcls.Update(existing);
        }
        else
        {
            var acl = new CollectionAcl
            {
                Id = Guid.NewGuid(),
                CollectionId = collectionId,
                PrincipalType = principalType,
                PrincipalId = principalId,
                Role = role,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.CollectionAcls.Add(acl);
            existing = acl;
        }

        await dbContext.SaveChangesAsync(ct);
        return existing;
    }

    public async Task RevokeAccessAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default)
    {
        var acl = await GetByPrincipalAsync(collectionId, principalType, principalId, ct);
        if (acl == null) return;

        dbContext.CollectionAcls.Remove(acl);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task RevokeAllAccessAsync(Guid collectionId, CancellationToken ct = default)
    {
        var acls = await dbContext.CollectionAcls
            .Where(a => a.CollectionId == collectionId)
            .ToListAsync(ct);

        dbContext.CollectionAcls.RemoveRange(acls);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<CollectionAcl>> GetAllAsync(CancellationToken ct = default)
    {
        return await dbContext.CollectionAcls
            .OrderBy(a => a.CollectionId)
            .ThenBy(a => a.PrincipalId)
            .ToListAsync(ct);
    }
}
