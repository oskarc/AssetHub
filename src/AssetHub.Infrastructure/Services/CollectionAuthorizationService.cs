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
/// Collections are flat — the effective role is the user's direct ACL grant on
/// that collection. Nesting and ACL inheritance were removed 2026-08
/// (contract-012).
/// </para>
/// <para>
/// Batch methods (<see cref="GetUserRolesAsync"/> / <see cref="FilterAccessibleAsync"/>)
/// pre-load the ancestor chain + the user's ACL rows in two round-trips and walk
/// in memory; per-collection methods reuse the same path with a single seed.
/// </para>
/// </remarks>
public sealed class CollectionAuthorizationService(
    DbContextProvider provider,
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
    /// loads the user's direct ACL grants across the uncached seeds in one query.
    /// Caches every resolved seed in <see cref="_roleCache"/>.
    /// </summary>
    /// <remarks>
    /// Collections are flat (nesting was removed 2026-08, contract-012), so the
    /// effective role IS the direct grant — there is no ancestor chain to walk.
    /// A collection with no grant for this user and a collection that does not
    /// exist both resolve to <c>null</c>, which is what the previous ancestor
    /// walk also returned for those cases.
    /// </remarks>
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

        var aclRows = await dbContext.CollectionAcls
            .AsNoTracking()
            .Where(a => uncached.Contains(a.CollectionId)
                && a.PrincipalType == PrincipalType.User
                && a.PrincipalId == userId)
            .Select(a => new { a.CollectionId, a.Role })
            .ToDictionaryAsync(a => a.CollectionId, a => a.Role.ToDbString(), ct);

        foreach (var seed in uncached)
        {
            var effective = aclRows.TryGetValue(seed, out var direct) ? direct : null;
            result[seed] = effective;
            _roleCache[$"{userId}:{seed}"] = effective;
        }

        logger.LogDebug(
            "Resolved {Seeds} seed collection(s) for {UserId} (direct grants {Grants})",
            uncached.Count, userId, aclRows.Count);

        return result;
    }
}
