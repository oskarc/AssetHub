using Dam.Domain.Entities;

namespace Dam.Application.Helpers;

/// <summary>
/// Determines the asset type from content type and file extension.
/// </summary>
public static class AssetTypeHelper
{
    /// <summary>
    /// Determines the asset type ("image", "video", "document") from the content type and file extension.
    /// </summary>
    public static string DetermineAssetType(string? contentType, string? extension)
    {
        // Check content type first
        if (!string.IsNullOrEmpty(contentType))
        {
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return Asset.TypeImage;
            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return Asset.TypeVideo;
            if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                return Asset.TypeDocument;
        }

        // Fall back to file extension
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" or ".tiff" or ".tif" or ".ico" => Asset.TypeImage,
            ".mp4" or ".avi" or ".mov" or ".wmv" or ".mkv" or ".webm" or ".flv" or ".m4v" => Asset.TypeVideo,
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" => Asset.TypeDocument,
            _ => Asset.TypeDocument
        };
    }
}
