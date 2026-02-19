using Dam.Domain.Entities;

namespace Dam.Application.Helpers;

/// <summary>
/// Determines the asset type from content type and file extension.
/// </summary>
public static class AssetTypeHelper
{
    /// <summary>
    /// Determines the asset type from the content type and file extension.
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
            if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                return AssetType.Document;
        }

        // Fall back to file extension
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" or ".tiff" or ".tif" or ".ico" => AssetType.Image,
            ".mp4" or ".avi" or ".mov" or ".wmv" or ".mkv" or ".webm" or ".flv" or ".m4v" => AssetType.Video,
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" => AssetType.Document,
            _ => AssetType.Document
        };
    }
}
