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
        public const string Group = "group";
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
    /// Image processing dimensions.
    /// </summary>
    public static class ImageDimensions
    {
        public const int ThumbnailWidth = 200;
        public const int ThumbnailHeight = 200;
        public const int MediumWidth = 800;
        public const int MediumHeight = 800;
    }
}
