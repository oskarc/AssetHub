namespace AssetHub.Infrastructure.Services;

using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Collection authorization service with request-scoped role caching.
/// Registered as Scoped — the private dictionary lives for exactly one HTTP request,
/// so revoked permissions take effect immediately on the next request.
/// System admins bypass all ACL checks and are treated as having the "admin" role on every collection.
/// </summary>
/// <remarks>
/// <para>
/// T5-NEST-01 — for collections with <c>InheritParentAcl = true</c> the effective
/// role is the highest of (direct grant on the collection, effective role on the
/// parent). The walk stops at the first ancestor with the flag set to <c>false</c>,
/// or at <see cref="Constants.Limits.MaxCollectionDepth"/> hops, whichever comes first.
/// Collections with the flag <c>false</c> (the default for every collection that
/// hasn't opted in) take the same fast path as today's flat model.
/// </para>
/// <para>
/// Batch methods (<see cref="GetUserRolesAsync"/> / <see cref="FilterAccessibleAsync"/>)
/// pre-load the ancestor chain + the user's ACL rows in two round-trips and walk
/// in memory; per-collection methods reuse the same path with a single seed.
/// </para>
/// </remarks>
public sealed class CollectionAuthorizationService(
    DbContextProvider provider,
    ICollectionRepository collectionRepo,
    CurrentUser currentUser,
    ILogger<CollectionAuthorizationService> logger) : ICollectionAuthorizationService
{
    // Request-scoped cache: userId:collectionId → effective role (or null).
    // Scoped lifetime guarantees this is discarded after each HTTP request,
    // so there is no stale-permission window across requests. Within a single
    // request, ACLs and inheritance flags are stable so the cached effective
    // role is safe to reuse.
    private readonly Dictionary<string, string?> _roleCache = new();

    public async Task<bool> CheckAccessAsync(string userId, Guid collectionId, string requiredRole, CancellationToken ct = default)
    {
        if (currentUser.IsSystemAdmin) return true;

        var userRole = await GetUserRoleAsync(userId, collectionId, ct);
        return RoleHierarchy.MeetsRequirement(userRole, requiredRole);
    }

    public async Task<string?> GetUserRoleAsync(string userId, Guid collectionId, CancellationToken ct = default)
    {
        if (currentUser.IsSystemAdmin) return RoleHierarchy.Roles.Admin;

        var cacheKey = $"{userId}:{collectionId}";
        if (_roleCache.TryGetValue(cacheKey, out var cachedRole))
        {
            logger.LogDebug("Request-scoped cache hit: {UserId} on {CollectionId} = {Role}", userId, collectionId, cachedRole);
            return cachedRole;
        }

        // Single-collection path uses the batch resolver under the hood — keeps
        // the inheritance walk in one place and means a per-collection caller
        // pays the same bounded ancestor-chain query as a batch caller would.
        var resolved = await ResolveRolesAsync(userId, new[] { collectionId }, ct);
        return resolved.GetValueOrDefault(collectionId);
    }

    public async Task<bool> CanManageAclAsync(string userId, Guid collectionId, CancellationToken ct = default)
    {
        if (currentUser.IsSystemAdmin) return true;
        return await CheckAccessAsync(userId, collectionId, RoleHierarchy.Roles.Manager, ct);
    }

    public Task<bool> CanCreateRootCollectionAsync(string userId)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(userId));
    }

    public async Task<Dictionary<Guid, string?>> GetUserRolesAsync(string userId, IEnumerable<Guid> collectionIds, CancellationToken ct = default)
    {
        var ids = collectionIds as IReadOnlyCollection<Guid> ?? collectionIds.ToList();
        if (ids.Count == 0) return new();

        if (currentUser.IsSystemAdmin)
            return ids.ToDictionary<Guid, Guid, string?>(id => id, _ => RoleHierarchy.Roles.Admin);

        return await ResolveRolesAsync(userId, ids, ct);
    }

    public async Task<List<Guid>> FilterAccessibleAsync(string userId, IEnumerable<Guid> collectionIds, string requiredRole, CancellationToken ct = default)
    {
        var ids = collectionIds as IReadOnlyCollection<Guid> ?? collectionIds.ToList();
        if (ids.Count == 0) return new();

        if (currentUser.IsSystemAdmin) return ids.ToList();

        var roles = await ResolveRolesAsync(userId, ids, ct);
        var accessible = new List<Guid>(ids.Count);
        foreach (var id in ids)
        {
            if (RoleHierarchy.MeetsRequirement(roles.GetValueOrDefault(id), requiredRole))
                accessible.Add(id);
        }
        return accessible;
    }

    /// <summary>
    /// Resolves the effective role for one user across <paramref name="seedIds"/>:
    /// loads each seed's ancestor chain bounded by <see cref="Constants.Limits.MaxCollectionDepth"/>,
    /// loads the user's direct ACL grants across the expanded set in one query,
    /// then walks each seed in memory (highest role wins, stop at non-inheriting node).
    /// Caches every resolved seed in <see cref="_roleCache"/>.
    /// </summary>
    private async Task<Dictionary<Guid, string?>> ResolveRolesAsync(
        string userId, IReadOnlyCollection<Guid> seedIds, CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var dbContext = lease.Db;
        // Skip seeds we already resolved this request — saves a round-trip
        // when callers re-ask for the same collection inside one request.
        var uncached = seedIds.Where(id => !_roleCache.ContainsKey($"{userId}:{id}")).Distinct().ToList();
        var result = new Dictionary<Guid, string?>(seedIds.Count);

        foreach (var id in seedIds)
        {
            if (_roleCache.TryGetValue($"{userId}:{id}", out var cached))
                result[id] = cached;
        }

        if (uncached.Count == 0) return result;

        // Pre-load: ancestor chain (id → parentId, inheritFlag) bounded by depth cap…
        var chain = await collectionRepo.GetAncestorChainAsync(uncached, ct);

        // …and direct ACL rows for the user across the expanded set.
        var expandedIds = chain.Keys.ToList();
        var aclRows = await dbContext.CollectionAcls
            .AsNoTracking()
            .Where(a => expandedIds.Contains(a.CollectionId)
                && a.PrincipalType == PrincipalType.User
                && a.PrincipalId == userId)
            .Select(a => new { a.CollectionId, a.Role })
            .ToDictionaryAsync(a => a.CollectionId, a => a.Role.ToDbString(), ct);

        // Walk each seed in memory and cache the result.
        foreach (var seed in uncached)
        {
            var effective = WalkEffectiveRole(seed, chain, aclRows);
            result[seed] = effective;
            _roleCache[$"{userId}:{seed}"] = effective;
        }

        logger.LogDebug(
            "Resolved {Seeds} seed collection(s) for {UserId} (chain {Chain}, direct grants {Grants})",
            uncached.Count, userId, chain.Count, aclRows.Count);

        return result;
    }

    /// <summary>
    /// Walks the parent chain in memory starting at <paramref name="seed"/>,
    /// returning the highest role found across the seed and any inheriting
    /// ancestors. Stops at the first ancestor with <c>InheritParentAcl = false</c>
    /// (that ancestor's ACL is still considered, but its parents are not) or at
    /// <see cref="Constants.Limits.MaxCollectionDepth"/> hops.
    /// </summary>
    private static string? WalkEffectiveRole(
        Guid seed,
        Dictionary<Guid, (Guid? ParentId, bool InheritParentAcl)> chain,
        Dictionary<Guid, string> aclRows)
    {
        if (!chain.ContainsKey(seed)) return null; // collection doesn't exist

        string? best = null;
        var current = seed;
        for (var depth = 0; depth <= Constants.Limits.MaxCollectionDepth; depth++)
        {
            if (aclRows.TryGetValue(current, out var direct)
                && RoleHierarchy.GetLevel(direct) > RoleHierarchy.GetLevel(best))
            {
                best = direct;
            }

            if (!chain.TryGetValue(current, out var node) || !node.InheritParentAcl || node.ParentId is null)
                break;

            current = node.ParentId.Value;
        }
        return best;
    }
}
