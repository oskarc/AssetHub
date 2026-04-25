namespace AssetHub.Application;

/// <summary>
/// Centralized cache key patterns, TTL configuration, and tag definitions for HybridCache.
/// 
/// NOTE: Authorization role lookups are NOT cached here.
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
    private const string CollectionAccessPrefix = "collection:access:";
    private const string CollectionNamePrefix = "collection:name:";
    private const string CollectionCountPrefix = "collection:count:";
    private const string DashboardSummaryPrefix = "dashboard:summary:";
    private const string ExportPresetPrefix = "export-preset:";
    private const string ExportPresetsAllKey = "export-presets:all";
    private const string MetadataSchemaPrefix = "metadata-schema:";
    private const string MetadataSchemasAllKey = "metadata-schemas:all";
    private const string TaxonomyPrefix = "taxonomy:";
    private const string TaxonomiesAllKey = "taxonomies:all";
    private const string UserRealmRolesPrefix = "user:realmroles:";
    private const string NotificationUnreadCountPrefix = "notif:unread-count:";

    // ── TTLs ──────────────────────────────────────────────────────────

    /// <summary>Collection IDs an asset belongs to. Invalidated on add/remove.</summary>
    public static readonly TimeSpan AssetCollectionIdsTtl = TimeSpan.FromMinutes(2);

    /// <summary>Username lookups from Keycloak user_entity. Rarely changes.</summary>
    public static readonly TimeSpan UserNameTtl = TimeSpan.FromMinutes(10);

    /// <summary>All users list (admin page). Short TTL since users can be created.</summary>
    public static readonly TimeSpan AllUsersTtl = TimeSpan.FromSeconds(30);

    /// <summary>Accessible collections per user. Invalidated on ACL changes.</summary>
    public static readonly TimeSpan CollectionAccessTtl = TimeSpan.FromMinutes(5);

    /// <summary>Collection name lookups. Invalidated on collection rename/delete.</summary>
    public static readonly TimeSpan CollectionNameTtl = TimeSpan.FromMinutes(10);

    /// <summary>Asset count per collection. Short TTL — volatile as assets are added/removed.</summary>
    public static readonly TimeSpan CollectionCountTtl = TimeSpan.FromMinutes(1);

    /// <summary>Dashboard summary data. Invalidated on asset/collection changes.</summary>
    public static readonly TimeSpan DashboardSummaryTtl = TimeSpan.FromMinutes(2);

    /// <summary>Export presets. Invalidated on preset create/update/delete.</summary>
    public static readonly TimeSpan ExportPresetTtl = TimeSpan.FromMinutes(10);

    /// <summary>Metadata schemas. Invalidated on schema create/update/delete.</summary>
    public static readonly TimeSpan MetadataSchemaTtl = TimeSpan.FromMinutes(10);

    /// <summary>Taxonomies. Invalidated on taxonomy create/update/delete.</summary>
    public static readonly TimeSpan TaxonomyTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Realm roles for a single user (PAT auth path). Short TTL — role
    /// changes in Keycloak take effect within this window without an
    /// explicit invalidation. Not an ACL lookup — those remain request-
    /// scoped per CLAUDE.md.
    /// </summary>
    /// <remarks>
    /// Security implication: a Keycloak user demoted from admin → viewer
    /// retains admin privileges on PAT-authenticated requests for up to
    /// this TTL. This is the deliberate compromise for performance —
    /// raising it past a minute or two would amplify the demotion-lag
    /// window beyond what most policies allow. Acknowledged in the
    /// security review (A-5). Per-PAT validity (revoke / expiry) is
    /// re-checked on every request via the DB; this cache only covers
    /// the realm-role lookup.
    /// </remarks>
    public static readonly TimeSpan UserRealmRolesTtl = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Unread notification count for the bell badge. Short TTL + tag invalidation on
    /// create/mark-read/delete keeps the badge fresh without hammering Postgres.
    /// </summary>
    public static readonly TimeSpan NotificationUnreadCountTtl = TimeSpan.FromMinutes(1);

    // ── Key Builders ──────────────────────────────────────────────────

    /// <summary>Cache key for the collection IDs an asset belongs to.</summary>
    public static string AssetCollectionIds(Guid assetId)
        => $"{AssetCollectionIdsPrefix}{assetId}";

    /// <summary>Cache key for a user's display name.</summary>
    public static string UserName(string userId)
        => $"{UserNamePrefix}{userId}";

    /// <summary>Cache key for the all-users list.</summary>
    public static string AllUsers() => AllUsersKey;

    /// <summary>Cache key for a user's accessible collection list.</summary>
    public static string CollectionAccess(string userId)
        => $"{CollectionAccessPrefix}{userId}";

    /// <summary>Cache key for a collection's name.</summary>
    public static string CollectionName(Guid collectionId)
        => $"{CollectionNamePrefix}{collectionId}";

    /// <summary>Cache key for a collection's asset count.</summary>
    public static string CollectionCount(Guid collectionId)
        => $"{CollectionCountPrefix}{collectionId}";

    /// <summary>Cache key for dashboard summary for a user.</summary>
    public static string DashboardSummary(string userId)
        => $"{DashboardSummaryPrefix}{userId}";

    /// <summary>Cache key for a single export preset.</summary>
    public static string ExportPreset(Guid id)
        => $"{ExportPresetPrefix}{id}";

    /// <summary>Cache key for the all export presets list.</summary>
    public static string ExportPresetsAll() => ExportPresetsAllKey;

    /// <summary>Cache key for a single metadata schema.</summary>
    public static string MetadataSchema(Guid id)
        => $"{MetadataSchemaPrefix}{id}";

    /// <summary>Cache key for the all metadata schemas list.</summary>
    public static string MetadataSchemasAll() => MetadataSchemasAllKey;

    /// <summary>Cache key for a single taxonomy.</summary>
    public static string Taxonomy(Guid id)
        => $"{TaxonomyPrefix}{id}";

    /// <summary>Cache key for the all taxonomies list.</summary>
    public static string TaxonomiesAll() => TaxonomiesAllKey;

    /// <summary>Cache key for a user's realm role set (used by PAT auth).</summary>
    public static string UserRealmRoles(string userId)
        => $"{UserRealmRolesPrefix}{userId}";

    /// <summary>Cache key for a user's unread-notification count (bell badge).</summary>
    public static string NotificationUnreadCount(string userId)
        => $"{NotificationUnreadCountPrefix}{userId}";

    // ── Tag Definitions (for HybridCache tag-based invalidation) ─────

    /// <summary>
    /// Tag definitions for group-based cache invalidation via HybridCache.RemoveByTagAsync().
    /// </summary>
    public static class Tags
    {
        /// <summary>Tag for asset-collection membership entries for a specific asset.</summary>
        public static string AssetCollections(Guid assetId) => $"asset-colls:{assetId}";

        /// <summary>Tag for all user name cache entries.</summary>
        public const string UserNames = "user-names";

        /// <summary>Tag for all collection-related entries for a specific collection.</summary>
        public static string Collection(Guid collectionId) => $"collection:{collectionId}";

        /// <summary>Tag for a user's collection access list entries.</summary>
        public static string CollectionAccessTag(string userId) => $"collection-access:{userId}";

        /// <summary>Tag for all collection ACL-related entries (invalidated on any ACL change).</summary>
        public const string CollectionAcl = "collection-acl";

        /// <summary>Tag for all dashboard summary entries.</summary>
        public const string Dashboard = "dashboard";

        /// <summary>Tag for all export preset entries.</summary>
        public const string ExportPresets = "export-presets";

        /// <summary>Tag for all metadata schema entries.</summary>
        public const string MetadataSchemas = "metadata-schemas";

        /// <summary>Tag for all taxonomy entries.</summary>
        public const string Taxonomies = "taxonomies";

        /// <summary>Tag for a user's notification entries (used to invalidate the unread-count cache).</summary>
        public static string NotificationsForUser(string userId) => $"notifications:{userId}";
    }
}
