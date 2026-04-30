using AssetHub.Domain.Entities;

namespace AssetHub.Application.Helpers;

/// <summary>
/// Determines the asset type from content type and file extension.
/// </summary>
public static class AssetTypeHelper
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".tiff", ".tif", ".ico"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".webm", ".flv", ".m4v"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".ogg", ".oga", ".opus", ".aac"
    };

    /// <summary>
    /// Determines the asset type from the content type and file extension.
    /// Anything that isn't recognised as image / video / audio falls through to
    /// <see cref="AssetType.Document"/>, matching the original behaviour.
    /// </summary>
    public static AssetType DetermineAssetType(string? contentType, string? extension)
    {
        // Check content type first
        if (!string.IsNullOrEmpty(contentType))
        {
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return AssetType.Image;
            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return AssetType.Video;
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                return AssetType.Audio;
            if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                return AssetType.Document;
        }

        if (extension is null) return AssetType.Document;
        if (ImageExtensions.Contains(extension)) return AssetType.Image;
        if (VideoExtensions.Contains(extension)) return AssetType.Video;
        if (AudioExtensions.Contains(extension)) return AssetType.Audio;
        return AssetType.Document;
    }
}
