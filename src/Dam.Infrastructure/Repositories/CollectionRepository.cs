namespace Dam.Infrastructure.Repositories;

using Dam.Application.Repositories;
using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public class CollectionRepository : ICollectionRepository
{
    private readonly AssetHubDbContext _dbContext;

    public CollectionRepository(AssetHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Collection?> GetByIdAsync(Guid id, bool includeAcls = false, bool includeChildren = false)
    {
        var query = _dbContext.Collections.AsQueryable();

        if (includeAcls)
            query = query.Include(c => c.Acls);

        if (includeChildren)
            query = query.Include(c => c.Children);

        return await query.FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<IEnumerable<Collection>> GetRootCollectionsAsync()
    {
        return await _dbContext.Collections
            .Where(c => c.ParentId == null)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Collection>> GetChildrenAsync(Guid parentId)
    {
        return await _dbContext.Collections
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Collection>> GetAccessibleCollectionsAsync(string userId)
    {
        // Get collections where user has direct ACL access
        return await _dbContext.Collections
            .Where(c => c.Acls.Any(a =>
                a.PrincipalType == "user" &&
                a.PrincipalId == userId))
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Collection> CreateAsync(Collection collection)
    {
        collection.Id = Guid.NewGuid();
        collection.CreatedAt = DateTime.UtcNow;

        _dbContext.Collections.Add(collection);
        await _dbContext.SaveChangesAsync();

        return collection;
    }

    public async Task<Collection> UpdateAsync(Collection collection)
    {
        _dbContext.Collections.Update(collection);
        await _dbContext.SaveChangesAsync();
        return collection;
    }

    public async Task DeleteAsync(Guid id)
    {
        var collection = await GetByIdAsync(id, includeChildren: true);
        if (collection == null) return;

        // Delete recursively
        await DeleteRecursiveAsync(collection);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await _dbContext.Collections.AnyAsync(c => c.Id == id);
    }

    public async Task<IEnumerable<Collection>> GetAllWithAclsAsync()
    {
        return await _dbContext.Collections
            .Include(c => c.Acls)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    private async Task DeleteRecursiveAsync(Collection collection)
    {
        // Delete all children recursively
        var children = await _dbContext.Collections
            .Where(c => c.ParentId == collection.Id)
            .ToListAsync();

        foreach (var child in children)
        {
            await DeleteRecursiveAsync(child);
        }

        _dbContext.Collections.Remove(collection);
    }
}

public class CollectionAclRepository : ICollectionAclRepository
{
    private readonly AssetHubDbContext _dbContext;

    public CollectionAclRepository(AssetHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<CollectionAcl>> GetByCollectionAsync(Guid collectionId)
    {
        return await _dbContext.CollectionAcls
            .Where(a => a.CollectionId == collectionId)
            .OrderBy(a => a.PrincipalType)
            .ThenBy(a => a.PrincipalId)
            .ToListAsync();
    }

    public async Task<CollectionAcl?> GetByPrincipalAsync(Guid collectionId, string principalType, string principalId)
    {
        return await _dbContext.CollectionAcls
            .FirstOrDefaultAsync(a =>
                a.CollectionId == collectionId &&
                a.PrincipalType == principalType &&
                a.PrincipalId == principalId);
    }

    public async Task<CollectionAcl> SetAccessAsync(Guid collectionId, string principalType, string principalId, string role)
    {
        var existing = await GetByPrincipalAsync(collectionId, principalType, principalId);

        if (existing != null)
        {
            // Update existing
            existing.Role = role;
            _dbContext.CollectionAcls.Update(existing);
        }
        else
        {
            // Create new
            var acl = new CollectionAcl
            {
                Id = Guid.NewGuid(),
                CollectionId = collectionId,
                PrincipalType = principalType,
                PrincipalId = principalId,
                Role = role,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.CollectionAcls.Add(acl);
            existing = acl;
        }

        await _dbContext.SaveChangesAsync();
        return existing;
    }

    public async Task RevokeAccessAsync(Guid collectionId, string principalType, string principalId)
    {
        var acl = await GetByPrincipalAsync(collectionId, principalType, principalId);
        if (acl == null) return;

        _dbContext.CollectionAcls.Remove(acl);
        await _dbContext.SaveChangesAsync();
    }

    public async Task RevokeAllAccessAsync(Guid collectionId)
    {
        var acls = await _dbContext.CollectionAcls
            .Where(a => a.CollectionId == collectionId)
            .ToListAsync();

        _dbContext.CollectionAcls.RemoveRange(acls);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<CollectionAcl>> GetAllAsync()
    {
        return await _dbContext.CollectionAcls
            .OrderBy(a => a.CollectionId)
            .ThenBy(a => a.PrincipalId)
            .ToListAsync();
    }
}
