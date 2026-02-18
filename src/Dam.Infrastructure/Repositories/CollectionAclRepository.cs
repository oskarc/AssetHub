namespace Dam.Infrastructure.Repositories;

using Dam.Application.Repositories;
using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        // Use a retry loop to handle the read-then-write race condition
        // when concurrent requests try to set access for the same principal.
        for (var attempt = 0; attempt < 3; attempt++)
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

            try
            {
                await dbContext.SaveChangesAsync(ct);
                return existing;
            }
            catch (DbUpdateException ex) when (
                attempt < 2 &&
                ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Unique constraint violation — another request inserted first.
                // Detach tracked entities and retry to update instead.
                foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Failed to set collection access after retries");
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
