namespace Dam.Infrastructure.Services;

using Dam.Application;
using Dam.Application.Services;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class CollectionAuthorizationService(
    AssetHubDbContext dbContext,
    ILogger<CollectionAuthorizationService> logger) : ICollectionAuthorizationService
{
    public async Task<bool> CheckAccessAsync(string userId, Guid collectionId, string requiredRole, CancellationToken ct = default)
    {
        var userRole = await GetUserRoleAsync(userId, collectionId, ct);
        return RoleHierarchy.MeetsRequirement(userRole, requiredRole);
    }

    public async Task<string?> GetUserRoleAsync(string userId, Guid collectionId, CancellationToken ct = default)
    {
        // Check if collection exists
        var collectionExists = await dbContext.Collections.AnyAsync(c => c.Id == collectionId, ct);
        if (!collectionExists)
        {
            logger.LogDebug($"Collection {collectionId} not found");
            return null;
        }

        // Look for direct ACL entry
        var acl = await dbContext.CollectionAcls
            .FirstOrDefaultAsync(a =>
                a.CollectionId == collectionId &&
                a.PrincipalType == "user" &&
                a.PrincipalId == userId, ct);

        return acl?.Role;
    }

    public async Task<bool> CanManageAclAsync(string userId, Guid collectionId, CancellationToken ct = default)
    {
        // User must have manager role or higher on the collection
        return await CheckAccessAsync(userId, collectionId, "manager", ct);
    }

    public async Task<bool> CanCreateRootCollectionAsync(string userId)
    {
        // For MVP: any authenticated user can create root collections
        return !string.IsNullOrEmpty(userId);
    }

    public async Task<bool> CanCreateSubCollectionAsync(string userId, Guid parentCollectionId, CancellationToken ct = default)
    {
        // User must have contributor role or higher on parent collection
        return await CheckAccessAsync(userId, parentCollectionId, "contributor", ct);
    }
}
