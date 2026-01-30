namespace Dam.Ui.Services;

/// <summary>
/// Utility methods for displaying assets in the UI.
/// Shared between AssetGrid, AllAssets, and other asset display components.
/// </summary>
public static class AssetDisplayHelpers
{
    /// <summary>
    /// Gets the thumbnail URL for an asset, or generates a placeholder SVG.
    /// </summary>
    public static string GetThumbnailUrl(Guid assetId, string? thumbObjectKey, string assetType)
    {
        if (!string.IsNullOrEmpty(thumbObjectKey))
        {
            return $"/api/assets/{assetId}/thumb";
        }

        return assetType switch
        {
            "image" => GetPlaceholderSvg("Image", "#4CAF50", "M21,19V5c0-1.1-0.9-2-2-2H5C3.9,3,3,3.9,3,5v14c0,1.1,0.9,2,2,2h14C20.1,21,21,20.1,21,19z M8.5,13.5l2.5,3.01L14.5,12l4.5,6H5L8.5,13.5z"),
            "video" => GetPlaceholderSvg("Video", "#2196F3", "M17,10.5V7c0-0.55-0.45-1-1-1H4C3.45,6,3,6.45,3,7v10c0,0.55,0.45,1,1,1h12c0.55,0,1-0.45,1-1v-3.5l4,4v-11L17,10.5z"),
            "document" => GetPlaceholderSvg("Document", "#FF9800", "M14,2H6C4.9,2,4.01,2.9,4.01,4L4,20c0,1.1,0.89,2,1.99,2H18c1.1,0,2-0.9,2-2V8L14,2z M16,18H8v-2h8V18z M16,14H8v-2h8V14z M13,9V3.5L18.5,9H13z"),
            _ => GetPlaceholderSvg("Asset", "#9E9E9E", "M6,2C4.89,2,4,2.9,4,4v16c0,1.1,0.89,2,2,2h12c1.1,0,2-0.9,2-2V8l-6-6H6z M13,9V3.5L18.5,9H13z")
        };
    }

    /// <summary>
    /// Generates an inline SVG data URL for a placeholder image.
    /// </summary>
    public static string GetPlaceholderSvg(string label, string color, string iconPath)
    {
        var svg = $@"<svg xmlns='http://www.w3.org/2000/svg' width='300' height='200' viewBox='0 0 300 200'>
            <rect fill='#f5f5f5' width='300' height='200'/>
            <g transform='translate(126, 60)'>
                <svg fill='{color}' viewBox='0 0 24 24' width='48' height='48'>
                    <path d='{iconPath}'/>
                </svg>
            </g>
            <text x='150' y='140' fill='{color}' font-family='sans-serif' font-size='16' text-anchor='middle'>{label}</text>
        </svg>";
        return $"data:image/svg+xml,{Uri.EscapeDataString(svg)}";
    }

    /// <summary>
    /// Gets the MudBlazor color for an asset type.
    /// </summary>
    public static MudBlazor.Color GetAssetTypeColor(string assetType)
    {
        return assetType switch
        {
            "image" => MudBlazor.Color.Success,
            "video" => MudBlazor.Color.Info,
            "document" => MudBlazor.Color.Warning,
            _ => MudBlazor.Color.Default
        };
    }

    /// <summary>
    /// Formats a byte count into a human-readable file size string.
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
