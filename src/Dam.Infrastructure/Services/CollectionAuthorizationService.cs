namespace Dam.Infrastructure.Services;

using Dam.Application;
using Dam.Application.Services;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Collection authorization service with request-scoped role caching.
/// Registered as Scoped — the private dictionary lives for exactly one HTTP request,
/// so revoked permissions take effect immediately on the next request.
/// </summary>
public class CollectionAuthorizationService(
    AssetHubDbContext dbContext,
    ILogger<CollectionAuthorizationService> logger) : ICollectionAuthorizationService
{
    // Request-scoped cache: userId:collectionId → role (or null).
    // Scoped lifetime guarantees this is discarded after each HTTP request,
    // so there is no stale-permission window across requests.
    private readonly Dictionary<string, string?> _roleCache = new();

    public async Task<bool> CheckAccessAsync(string userId, Guid collectionId, string requiredRole, CancellationToken ct = default)
    {
        var userRole = await GetUserRoleAsync(userId, collectionId, ct);
        return RoleHierarchy.MeetsRequirement(userRole, requiredRole);
    }

    public async Task<string?> GetUserRoleAsync(string userId, Guid collectionId, CancellationToken ct = default)
    {
        var cacheKey = $"{userId}:{collectionId}";

        if (_roleCache.TryGetValue(cacheKey, out var cachedRole))
        {
            logger.LogDebug("Request-scoped cache hit: {UserId} on {CollectionId} = {Role}", userId, collectionId, cachedRole);
            return cachedRole;
        }

        // Check if collection exists
        var collectionExists = await dbContext.Collections.AnyAsync(c => c.Id == collectionId, ct);
        if (!collectionExists)
        {
            logger.LogDebug("Collection {CollectionId} not found", collectionId);
            _roleCache[cacheKey] = null;
            return null;
        }

        // Look for direct ACL entry
        var acl = await dbContext.CollectionAcls
            .FirstOrDefaultAsync(a =>
                a.CollectionId == collectionId &&
                a.PrincipalType == "user" &&
                a.PrincipalId == userId, ct);

        var role = acl?.Role;

        // If no direct ACL, walk up the parent chain to find inherited access.
        // Max depth guard prevents infinite loops from corrupted circular ParentId chains.
        const int maxDepth = 20;
        if (role == null)
        {
            var currentId = collectionId;
            var depth = 0;
            while (role == null && depth < maxDepth)
            {
                depth++;
                var parentId = await dbContext.Collections
                    .Where(c => c.Id == currentId)
                    .Select(c => c.ParentId)
                    .FirstOrDefaultAsync(ct);

                if (parentId == null) break;

                // Check cache for the parent first
                var parentCacheKey = $"{userId}:{parentId.Value}";
                if (_roleCache.TryGetValue(parentCacheKey, out var parentCachedRole))
                {
                    role = parentCachedRole;
                    break;
                }

                var parentAcl = await dbContext.CollectionAcls
                    .FirstOrDefaultAsync(a =>
                        a.CollectionId == parentId.Value &&
                        a.PrincipalType == "user" &&
                        a.PrincipalId == userId, ct);

                if (parentAcl != null)
                {
                    role = parentAcl.Role;
                    // Cache the parent's role too
                    _roleCache[parentCacheKey] = role;
                }

                currentId = parentId.Value;
            }

            if (depth >= maxDepth)
            {
                logger.LogWarning("Max parent-chain depth ({MaxDepth}) reached for collection {CollectionId}. Possible circular ParentId.", maxDepth, collectionId);
            }

            if (role != null)
            {
                logger.LogDebug("Inherited role for {UserId} on {CollectionId}: {Role}", userId, collectionId, role);
            }
        }

        _roleCache[cacheKey] = role;
        return role;
    }

    public async Task<bool> IsRoleInheritedAsync(string userId, Guid collectionId, CancellationToken ct = default)
    {
        // Check if there is a direct ACL entry for this user on this collection
        var hasDirectAcl = await dbContext.CollectionAcls
            .AnyAsync(a =>
                a.CollectionId == collectionId &&
                a.PrincipalType == "user" &&
                a.PrincipalId == userId, ct);

        return !hasDirectAcl;
    }

    public async Task<bool> CanManageAclAsync(string userId, Guid collectionId, CancellationToken ct = default)
    {
        // User must have manager role or higher on the collection
        return await CheckAccessAsync(userId, collectionId, "manager", ct);
    }

    public async Task<bool> CanCreateRootCollectionAsync(string userId)
    {
        // Only managers and admins can create root collections.
        // Contributors should only be added to existing collections.
        // This is invoked after the caller has already verified the user has
        // at least the "manager" Keycloak role (checked at the endpoint level).
        return !string.IsNullOrEmpty(userId);
    }

    public async Task<bool> CanCreateSubCollectionAsync(string userId, Guid parentCollectionId, CancellationToken ct = default)
    {
        // User must have contributor role or higher on parent collection
        return await CheckAccessAsync(userId, parentCollectionId, "contributor", ct);
    }
}
