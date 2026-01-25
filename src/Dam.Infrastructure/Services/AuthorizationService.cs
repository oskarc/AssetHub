namespace Dam.Infrastructure.Services;

using Dam.Application.Services;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public class CollectionAuthorizationService : ICollectionAuthorizationService
{
    private readonly AssetHubDbContext _dbContext;

    // Role hierarchy for permission checking
    private static readonly Dictionary<string, int> RoleHierarchy = new()
    {
        { "viewer", 1 },
        { "contributor", 2 },
        { "manager", 3 },
        { "admin", 4 }
    };

    public CollectionAuthorizationService(AssetHubDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> CheckAccessAsync(string userId, Guid collectionId, string requiredRole)
    {
        var userRole = await GetUserRoleAsync(userId, collectionId);
        if (userRole == null) return false;

        // Check if user's role meets or exceeds required role
        if (!RoleHierarchy.TryGetValue(userRole, out var userLevel) ||
            !RoleHierarchy.TryGetValue(requiredRole, out var requiredLevel))
        {
            return false;
        }

        return userLevel >= requiredLevel;
    }

    public async Task<string?> GetUserRoleAsync(string userId, Guid collectionId)
    {
        // Check if collection exists
        var collectionExists = await _dbContext.Collections.AnyAsync(c => c.Id == collectionId);
        if (!collectionExists) return null;

        // Look for direct ACL entry
        var acl = await _dbContext.CollectionAcls
            .FirstOrDefaultAsync(a =>
                a.CollectionId == collectionId &&
                a.PrincipalType == "user" &&
                a.PrincipalId == userId);

        return acl?.Role;
    }

    public async Task<bool> CanManageAclAsync(string userId, Guid collectionId)
    {
        // User must have manager role or higher on the collection
        return await CheckAccessAsync(userId, collectionId, "manager");
    }

    public async Task<bool> CanCreateRootCollectionAsync(string userId)
    {
        // For MVP: any authenticated user can create root collections
        return !string.IsNullOrEmpty(userId);
    }

    public async Task<bool> CanCreateSubCollectionAsync(string userId, Guid parentCollectionId)
    {
        // User must have contributor role or higher on parent collection
        return await CheckAccessAsync(userId, parentCollectionId, "contributor");
    }
}
