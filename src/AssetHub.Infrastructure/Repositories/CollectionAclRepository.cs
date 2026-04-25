namespace AssetHub.Infrastructure.Repositories;

using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

public sealed class CollectionAclRepository(
    AssetHubDbContext dbContext,
    ILogger<CollectionAclRepository> logger) : ICollectionAclRepository
{
    public async Task<IEnumerable<CollectionAcl>> GetByCollectionAsync(Guid collectionId, CancellationToken ct = default)
    {
        return await dbContext.CollectionAcls
            .AsNoTracking()
            .Where(a => a.CollectionId == collectionId)
            .OrderBy(a => a.PrincipalType)
            .ThenBy(a => a.PrincipalId)
            .ToListAsync(ct);
    }

    public async Task<CollectionAcl?> GetByPrincipalAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default)
    {
        var pt = Enum.Parse<PrincipalType>(principalType, true);
        return await dbContext.CollectionAcls
            .FirstOrDefaultAsync(a =>
                a.CollectionId == collectionId &&
                a.PrincipalType == pt &&
                a.PrincipalId == principalId, ct);
    }
    
    public async Task<IEnumerable<CollectionAcl>> GetByUserAsync(string userId, CancellationToken ct = default)
    {
        return await dbContext.CollectionAcls
            .AsNoTracking()
            .Where(a => a.PrincipalType == PrincipalType.User && a.PrincipalId == userId)
            .ToListAsync(ct);
    }

    public async Task<CollectionAcl> SetAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default)
    {
        // Use a retry loop to handle the read-then-write race condition
        // when concurrent requests try to set access for the same principal.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existing = await GetByPrincipalAsync(collectionId, principalType, principalId, ct);

            if (existing is not null)
            {
                existing.Role = Enum.Parse<AclRole>(role, true);
                dbContext.CollectionAcls.Update(existing);
            }
            else
            {
                var acl = new CollectionAcl
                {
                    Id = Guid.NewGuid(),
                    CollectionId = collectionId,
                    PrincipalType = Enum.Parse<PrincipalType>(principalType, true),
                    PrincipalId = principalId,
                    Role = Enum.Parse<AclRole>(role, true),
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.CollectionAcls.Add(acl);
                existing = acl;
            }

            try
            {
                await dbContext.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Set {Role} access for {PrincipalType} {PrincipalId} on collection {CollectionId}",
                    role, principalType, principalId, collectionId);
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
        if (acl is null)
        {
            logger.LogDebug(
                "No ACL found to revoke for {PrincipalType} {PrincipalId} on collection {CollectionId}",
                principalType, principalId, collectionId);
            return;
        }

        dbContext.CollectionAcls.Remove(acl);
        await dbContext.SaveChangesAsync(ct);
        logger.LogInformation(
            "Revoked access for {PrincipalType} {PrincipalId} on collection {CollectionId}",
            principalType, principalId, collectionId);
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
            .AsNoTracking()
            .OrderBy(a => a.CollectionId)
            .ThenBy(a => a.PrincipalId)
            .ToListAsync(ct);
    }

    public async Task<int> DeleteByUserAsync(string userId, CancellationToken ct = default)
    {
        return await dbContext.CollectionAcls
            .Where(a => a.PrincipalType == PrincipalType.User && a.PrincipalId == userId)
            .ExecuteDeleteAsync(ct);
    }
}
