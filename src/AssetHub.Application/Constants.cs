using System.Collections.Frozen;

namespace AssetHub.Application;

/// <summary>
/// Centralized constants for the DAM application.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Authorization policy names.
    /// </summary>
    public static class Policies
    {
        public const string RequireAdmin = nameof(RequireAdmin);
    }

    /// <summary>
    /// Rate-limiting policy names used in endpoint configuration and middleware.
    /// </summary>
    public static class RateLimitPolicies
    {
        public const string BlazorSignalR = nameof(BlazorSignalR);
        public const string ShareAnonymous = nameof(ShareAnonymous);
        public const string SharePassword = nameof(SharePassword);
    }

    /// <summary>
    /// Principal types for ACL entries.
    /// </summary>
    public static class PrincipalTypes
    {
        public const string User = "user";
    }

    /// <summary>
    /// Scope types for share links and audit log target types.
    /// </summary>
    public static class ScopeTypes
    {
        public const string Asset = "asset";
        public const string Collection = "collection";
        public const string Share = "share";
        public const string User = "user";
        public const string Migration = "migration";
        public const string UserPreferences = "user_preferences";
        public const string Comment = "comment";
        public const string Webhook = "webhook";
        public const string WebhookDelivery = "webhook_delivery";
        public const string Brand = "brand";
    }

    /// <summary>
    /// Data Protection purpose strings.
    /// </summary>
    public static class DataProtection
    {
        public const string ShareTokenProtector = "ShareTokenProtector";
        public const string ShareAccessTokenProtector = "ShareAccessTokenProtector";
        public const string SharePasswordProtector = "SharePasswordProtector";
        public const string MigrationSourceSecretProtector = "MigrationSourceSecretProtector";
        public const string NotificationUnsubscribeProtector = "NotificationUnsubscribeProtector";
        public const string WebhookSecretProtector = "WebhookSecretProtector";
    }

    /// <summary>
    /// Object key prefixes for MinIO storage.
    /// </summary>
    public static class StoragePrefixes
    {
        public const string Originals = "originals";
        public const string Thumbnails = "thumbs";
        public const string Medium = "medium";
        public const string Posters = "posters";
        public const string TempZipDownloads = "zip-downloads";
        public const string Brands = "brands";
    }

    /// <summary>
    /// Sort-order values accepted by asset search endpoints.
    /// </summary>
    public static class SortBy
    {
        public const string CreatedDesc = "created_desc";
        public const string CreatedAsc = "created_asc";
        public const string TitleAsc = "title_asc";
        public const string TitleDesc = "title_desc";
        public const string SizeAsc = "size_asc";
        public const string SizeDesc = "size_desc";
    }

    /// <summary>
    /// Asset-type filter values (match DB string representation).
    /// </summary>
    public static class AssetTypeFilters
    {
        public const string Image = "image";
        public const string Video = "video";
        public const string Document = "document";
        public const string Audio = "audio";
    }

    /// <summary>
    /// Asset processing status values.
    /// </summary>
    public static class AssetStatus
    {
        public const string Ready = "ready";
        public const string Processing = "processing";
        public const string Failed = "failed";
    }

    /// <summary>
    /// View-mode identifiers for grid/list toggling.
    /// </summary>
    public static class ViewModes
    {
        public const string Grid = "grid";
        public const string List = "list";
    }

    /// <summary>
    /// Zip-download progress status strings (sent from JS interop).
    /// </summary>
    public static class ZipProgressStatus
    {
        public const string Pending = "pending";
        public const string Building = "building";
    }

    /// <summary>
    /// Share link status values.
    /// </summary>
    public static class ShareStatus
    {
        public const string Active = "Active";
        public const string Expired = "Expired";
        public const string Revoked = "Revoked";
    }

    /// <summary>
    /// API error codes for share link access errors.
    /// </summary>
    public static class ShareErrorCodes
    {
        public const string Expired = "SHARE_EXPIRED";
        public const string Revoked = "SHARE_REVOKED";
    }

    /// <summary>
    /// Common content types.
    /// </summary>
    public static class ContentTypes
    {
        public const string Jpeg = "image/jpeg";
        public const string Png = "image/png";
        public const string Gif = "image/gif";
        public const string Webp = "image/webp";
        public const string Pdf = "application/pdf";
        public const string Mp4 = "video/mp4";
        public const string Webm = "video/webm";
    }

    /// <summary>
    /// Content-type prefixes and specific types allowed for upload.
    /// Blocks executable, script, and other potentially dangerous file types.
    /// </summary>
    public static class AllowedUploadTypes
    {
        /// <summary>MIME type prefixes that are allowed (e.g. "image/" matches all image/* types).</summary>
        private static readonly string[] AllowedPrefixes =
        [
            "image/",
            "video/",
            "audio/",
        ];

        /// <summary>Specific MIME types allowed in addition to prefix matches.</summary>
        public static readonly FrozenSet<string> AllowedExact = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "application/zip",
            "application/x-zip-compressed",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",   // .docx
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",          // .xlsx
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",  // .pptx
            "application/msword",                                                          // .doc
            "application/vnd.ms-excel",                                                    // .xls
            "application/vnd.ms-powerpoint",                                               // .ppt
            "text/plain",
            "text/csv",
            "application/json",
            "application/xml",
            "text/xml",
            // NOTE: application/svg+xml removed due to XSS risk — SVGs can contain embedded JavaScript
            "font/woff",
            "font/woff2",
            "font/ttf",
            "font/otf",
            "application/x-font-woff",
            "model/gltf-binary",
            "model/gltf+json",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns true if the given content type is allowed for upload.
        /// </summary>
        public static bool IsAllowed(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            return AllowedExact.Contains(contentType)
                || AllowedPrefixes.Any(prefix => contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Application-wide numeric limits and default values.
    /// </summary>
    public static class Limits
    {
        /// <summary>Presigned download URL expiry in seconds (5 minutes).</summary>
        public const int PresignedDownloadExpirySec = 300;

        /// <summary>Presigned upload URL expiry in seconds (15 minutes).</summary>
        public const int PresignedUploadExpirySec = 900;

        /// <summary>Maximum assets that can be included in a zip download.</summary>
        public const int MaxDownloadableAssets = 1000;

        /// <summary>Default maximum upload size in MB.</summary>
        public const int DefaultMaxUploadSizeMb = 1500;

        /// <summary>Maximum image size in MB for the browser-based image editor.</summary>
        public const int MaxEditorImageSizeMb = 50;

        /// <summary>
        /// Maximum migration manifest (CSV / JSONL) size in MB. Deliberately
        /// small — a manifest is metadata only, not bytes. Admin-only upload.
        /// </summary>
        public const int MaxMigrationManifestSizeMb = 50;

        /// <summary>
        /// Maximum per-file size in MB during migration staging uploads.
        /// Tighter than <see cref="DefaultMaxUploadSizeMb"/> because migration
        /// bulk uploads fan out many files in parallel and we don't want any
        /// single one to dominate the client/server link.
        /// </summary>
        public const int MaxMigrationFileSizeMb = 500;

        /// <summary>Maximum page size for paginated list endpoints.</summary>
        public const int MaxPageSize = 200;

        /// <summary>How long completed ZIP downloads remain available (hours).</summary>
        public const int ZipDownloadExpiryHours = 1;

        /// <summary>Maximum concurrent ZIP builds per user.</summary>
        public const int MaxConcurrentZipBuilds = 3;

        /// <summary>Maximum share link expiry in days.</summary>
        public const int MaxShareExpiryDays = 90;

        /// <summary>Minimum character length for share link passwords.</summary>
        public const int MinSharePasswordLength = 8;

        /// <summary>How long a share access token remains valid (minutes).</summary>
        public const int ShareAccessTokenLifetimeMinutes = 30;

        /// <summary>Maximum number of entries in the in-memory cache.</summary>
        public const int MemoryCacheSizeLimit = 10_000;

        /// <summary>
        /// Hard cap for admin queries that load all collections with ACLs in one shot
        /// (e.g. the admin access-tree and user-list views). Prevents unbounded memory
        /// use if the collection count grows large.
        /// </summary>
        public const int AdminCollectionQueryLimit = 2_000;

        /// <summary>
        /// Hard cap for admin share list queries. Prevents unbounded memory use if the
        /// share count grows large; the admin UI supports paginated "load more" access
        /// beyond this window.
        /// </summary>
        public const int AdminShareQueryLimit = 500;

        /// <summary>
        /// Default page size for admin list endpoints (shares, audit log).
        /// </summary>
        public const int DefaultAdminPageSize = 50;

        /// <summary>
        /// Maximum number of users loaded in a single query from the Keycloak database.
        /// Prevents unbounded memory use in large deployments (CWE-400).
        /// </summary>
        public const int MaxUserQueryLimit = 10_000;

        /// <summary>Maximum number of tags per asset.</summary>
        public const int MaxTagsPerAsset = 50;

        /// <summary>Maximum character length for a single asset tag.</summary>
        public const int MaxTagLength = 100;

        /// <summary>Maximum number of entries in the asset metadata dictionary.</summary>
        public const int MaxMetadataEntries = 200;

        /// <summary>Maximum character length for a single metadata key.</summary>
        public const int MaxMetadataKeyLength = 100;

        /// <summary>Maximum character length for a single metadata value (string representation).</summary>
        public const int MaxMetadataValueLength = 1000;

        /// <summary>Maximum number of files that can be uploaded in a single batch.</summary>
        public const int MaxFilesPerUpload = 10;

        /// <summary>
        /// Soft cap used when counting audit events for display.
        /// We fetch at most this many rows before stopping the count query,
        /// so the UI shows "10 000+" rather than scanning the entire table.
        /// </summary>
        public const int AuditCountDisplayCap = 10_000;

        /// <summary>
        /// Number of days to retain audit events. Events older than this are deleted
        /// by the <c>AuditRetentionJob</c> recurring background job.
        /// </summary>
        public const int AuditRetentionDays = 365;
    }

    /// <summary>
    /// Client-side route paths.
    /// </summary>
    public static class Routes
    {
        /// <summary>
        /// Public share page route prefix (without leading slash).
        /// Full path: /share/{token}
        /// </summary>
        public const string Share = "share";
    }
}
