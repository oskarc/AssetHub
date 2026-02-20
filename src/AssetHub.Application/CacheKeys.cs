using Microsoft.Extensions.Caching.Memory;

namespace AssetHub.Application;

/// <summary>
/// Centralized cache key patterns and TTL configuration for IMemoryCache.
/// 
/// NOTE: Authorization role lookups are NOT cached in IMemoryCache.
/// They use request-scoped caching (private Dictionary in the Scoped
/// CollectionAuthorizationService) to avoid stale-permission windows.
/// Only non-security-critical data is cached here.
/// </summary>
public static class CacheKeys
{
    // ── Key Prefixes ──────────────────────────────────────────────────
    private const string AssetCollectionIdsPrefix = "asset:colls:";
    private const string UserNamePrefix = "user:name:";
    private const string AllUsersKey = "users:all";

    // ── TTLs ──────────────────────────────────────────────────────────

    /// <summary>Collection IDs an asset belongs to. Invalidated on add/remove.</summary>
    public static readonly TimeSpan AssetCollectionIdsTtl = TimeSpan.FromMinutes(2);

    /// <summary>Username lookups from Keycloak user_entity. Rarely changes.</summary>
    public static readonly TimeSpan UserNameTtl = TimeSpan.FromMinutes(10);

    /// <summary>All users list (admin page). Short TTL since users can be created.</summary>
    public static readonly TimeSpan AllUsersTtl = TimeSpan.FromSeconds(30);

    // ── Key Builders ──────────────────────────────────────────────────

    /// <summary>Cache key for the collection IDs an asset belongs to.</summary>
    public static string AssetCollectionIds(Guid assetId)
        => $"{AssetCollectionIdsPrefix}{assetId}";

    /// <summary>Cache key for a user's display name.</summary>
    public static string UserName(string userId)
        => $"{UserNamePrefix}{userId}";

    /// <summary>Cache key for the all-users list.</summary>
    public static string AllUsers() => AllUsersKey;

    // ── Invalidation Helpers ──────────────────────────────────────────

    /// <summary>Invalidate cached collection IDs for an asset.</summary>
    public static void InvalidateAssetCollectionIds(IMemoryCache cache, Guid assetId)
    {
        cache.Remove(AssetCollectionIds(assetId));
    }

    /// <summary>Invalidate cached all-users list.</summary>
    public static void InvalidateAllUsers(IMemoryCache cache)
    {
        cache.Remove(AllUsers());
    }

    /// <summary>Invalidate cached username for a user.</summary>
    public static void InvalidateUserName(IMemoryCache cache, string userId)
    {
        cache.Remove(UserName(userId));
    }
}
