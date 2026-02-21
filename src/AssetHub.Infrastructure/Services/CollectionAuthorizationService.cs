namespace AssetHub.Infrastructure.Services;

using AssetHub.Application;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
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
                a.PrincipalType == PrincipalType.User &&
                a.PrincipalId == userId, ct);

        var role = acl?.Role.ToDbString();

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
                        a.PrincipalType == PrincipalType.User &&
                        a.PrincipalId == userId, ct);

                if (parentAcl != null)
                {
                    role = parentAcl.Role.ToDbString();
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
                a.PrincipalType == PrincipalType.User &&
                a.PrincipalId == userId, ct);

        return !hasDirectAcl;
    }

    public async Task<bool> CanManageAclAsync(string userId, Guid collectionId, CancellationToken ct = default)
    {
        // User must have manager role or higher on the collection
        return await CheckAccessAsync(userId, collectionId, "manager", ct);
    }

    public Task<bool> CanCreateRootCollectionAsync(string userId)
    {
        // Any authenticated user may create root collections.
        // Non-admin users must still have appropriate access to create sub-collections.
        return Task.FromResult(!string.IsNullOrWhiteSpace(userId));
    }

    public async Task<bool> CanCreateSubCollectionAsync(string userId, Guid parentCollectionId, CancellationToken ct = default)
    {
        // User must have contributor role or higher on parent collection
        return await CheckAccessAsync(userId, parentCollectionId, "contributor", ct);
    }

    public async Task<Dictionary<Guid, string?>> GetUserRolesAsync(string userId, IEnumerable<Guid> collectionIds, CancellationToken ct = default)
    {
        var ids = collectionIds.ToList();
        if (ids.Count == 0) return new();

        // Pre-warm the cache by loading all direct ACLs for this user in one query
        await PreloadUserAclsAsync(userId, ids, ct);

        var result = new Dictionary<Guid, string?>(ids.Count);
        foreach (var collectionId in ids)
        {
            result[collectionId] = await GetUserRoleAsync(userId, collectionId, ct);
        }
        return result;
    }

    public async Task<List<Guid>> FilterAccessibleAsync(string userId, IEnumerable<Guid> collectionIds, string requiredRole, CancellationToken ct = default)
    {
        var ids = collectionIds.ToList();
        if (ids.Count == 0) return new();

        // Pre-warm the cache by loading all direct ACLs for this user in one query
        await PreloadUserAclsAsync(userId, ids, ct);

        var accessible = new List<Guid>();
        foreach (var collectionId in ids)
        {
            if (await CheckAccessAsync(userId, collectionId, requiredRole, ct))
                accessible.Add(collectionId);
        }
        return accessible;
    }

    public async Task<Dictionary<Guid, bool>> AreRolesInheritedAsync(string userId, IEnumerable<Guid> collectionIds, CancellationToken ct = default)
    {
        var ids = collectionIds.ToList();
        if (ids.Count == 0) return new();

        // Single query: check which collections have a direct ACL for this user
        var directAclCollectionIds = await dbContext.CollectionAcls
            .Where(a => ids.Contains(a.CollectionId) &&
                        a.PrincipalType == PrincipalType.User &&
                        a.PrincipalId == userId)
            .Select(a => a.CollectionId)
            .ToHashSetAsync(ct);

        return ids.ToDictionary(id => id, id => !directAclCollectionIds.Contains(id));
    }

    /// <summary>
    /// Pre-loads all direct ACL entries for a user on the given collections into the request-scoped cache.
    /// This converts N individual ACL queries into a single batch query.
    /// </summary>
    private async Task PreloadUserAclsAsync(string userId, List<Guid> collectionIds, CancellationToken ct)
    {
        // Only query for collections not already in cache
        var uncachedIds = collectionIds
            .Where(id => !_roleCache.ContainsKey($"{userId}:{id}"))
            .ToList();

        if (uncachedIds.Count == 0) return;

        // Single query: get all direct ACLs for this user on these collections
        var directAcls = await dbContext.CollectionAcls
            .Where(a => uncachedIds.Contains(a.CollectionId) &&
                        a.PrincipalType == PrincipalType.User &&
                        a.PrincipalId == userId)
            .ToDictionaryAsync(a => a.CollectionId, a => a.Role.ToDbString(), ct);

        // Cache direct hits (but don't cache misses — they may have inherited roles from parents)
        foreach (var (collId, role) in directAcls)
        {
            _roleCache[$"{userId}:{collId}"] = role;
        }

        logger.LogDebug("Pre-loaded {Count} direct ACLs for user {UserId} across {Total} collections",
            directAcls.Count, userId, uncachedIds.Count);
    }
}
