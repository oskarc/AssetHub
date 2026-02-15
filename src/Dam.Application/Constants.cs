namespace Dam.Application;

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
    /// Principal types for ACL entries.
    /// </summary>
    public static class PrincipalTypes
    {
        public const string User = "user";
    }

    /// <summary>
    /// Scope types for share links.
    /// </summary>
    public static class ScopeTypes
    {
        public const string Asset = "asset";
        public const string Collection = "collection";
    }

    /// <summary>
    /// Data Protection purpose strings.
    /// </summary>
    public static class DataProtection
    {
        public const string ShareTokenProtector = "ShareTokenProtector";
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
        public const int DefaultMaxUploadSizeMb = 500;
    }
}
