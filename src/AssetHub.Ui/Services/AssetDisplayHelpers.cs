using AssetHub.Application;
using AssetHub.Application.Helpers;

namespace AssetHub.Ui.Services;

/// <summary>
/// Utility methods for displaying assets, shares, and roles in the UI.
/// Shared between all pages and components to eliminate duplication.
/// </summary>
public static class AssetDisplayHelpers
{
    // ===== THUMBNAILS & PLACEHOLDERS =====

    /// <summary>
    /// Gets the thumbnail URL for an asset, or generates a placeholder SVG.
    /// </summary>
    public static string GetThumbnailUrl(Guid assetId, string? thumbObjectKey, string assetType)
    {
        if (!string.IsNullOrEmpty(thumbObjectKey))
        {
            return $"/api/assets/{assetId}/thumb";
        }

        return GetPlaceholderForType(assetType);
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
    /// Gets a placeholder SVG image for a given asset type (image/video/document).
    /// Use this when you have an asset type but no thumbnail URL.
    /// </summary>
    public static string GetPlaceholderForType(string? assetType)
    {
        return assetType switch
        {
            "image" => GetPlaceholderSvg("Image", "#4CAF50", "M21,19V5c0-1.1-0.9-2-2-2H5C3.9,3,3,3.9,3,5v14c0,1.1,0.9,2,2,2h14C20.1,21,21,20.1,21,19z M8.5,13.5l2.5,3.01L14.5,12l4.5,6H5L8.5,13.5z"),
            "video" => GetPlaceholderSvg("Video", "#2196F3", "M17,10.5V7c0-0.55-0.45-1-1-1H4C3.45,6,3,6.45,3,7v10c0,0.55,0.45,1,1,1h12c0.55,0,1-0.45,1-1v-3.5l4,4v-11L17,10.5z"),
            "document" => GetPlaceholderSvg("Document", "#FF9800", "M14,2H6C4.9,2,4.01,2.9,4.01,4L4,20c0,1.1,0.89,2,1.99,2H18c1.1,0,2-0.9,2-2V8L14,2z M16,18H8v-2h8V18z M16,14H8v-2h8V14z M13,9V3.5L18.5,9H13z"),
            _ => GetPlaceholderSvg("Asset", "#9E9E9E", "M6,2C4.89,2,4,2.9,4,4v16c0,1.1,0.89,2,2,2h12c1.1,0,2-0.9,2-2V8l-6-6H6z M13,9V3.5L18.5,9H13z")
        };
    }

    // ===== ASSET TYPE DISPLAY =====

    /// <summary>
    /// Gets the MudBlazor color for an asset type.
    /// </summary>
    public static MudBlazor.Color GetAssetTypeColor(string? assetType)
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
    /// Gets the MudBlazor icon string for an asset type.
    /// </summary>
    public static string GetAssetIcon(string? assetType)
    {
        return assetType switch
        {
            "image" => MudBlazor.Icons.Material.Filled.Image,
            "video" => MudBlazor.Icons.Material.Filled.VideoFile,
            "document" => MudBlazor.Icons.Material.Filled.Description,
            _ => MudBlazor.Icons.Material.Filled.InsertDriveFile
        };
    }

    /// <summary>
    /// Gets the MudBlazor icon string based on a MIME content type.
    /// Use this when you have a content type string rather than a resolved asset type.
    /// </summary>
    public static string GetContentTypeIcon(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return MudBlazor.Icons.Material.Filled.InsertDriveFile;

        if (contentType.StartsWith("image/"))
            return MudBlazor.Icons.Material.Filled.Image;
        if (contentType.StartsWith("video/"))
            return MudBlazor.Icons.Material.Filled.VideoFile;
        if (contentType.Contains("pdf"))
            return MudBlazor.Icons.Material.Filled.PictureAsPdf;

        return MudBlazor.Icons.Material.Filled.InsertDriveFile;
    }

    // ===== STATUS COLORS =====

    /// <summary>
    /// Gets the MudBlazor color for an asset processing status.
    /// </summary>
    public static MudBlazor.Color GetAssetStatusColor(string? status)
    {
        return status switch
        {
            "ready" => MudBlazor.Color.Success,
            "processing" => MudBlazor.Color.Info,
            "failed" => MudBlazor.Color.Error,
            _ => MudBlazor.Color.Default
        };
    }

    /// <summary>
    /// Gets the MudBlazor color for a share status (Active/Expired/Revoked).
    /// </summary>
    public static MudBlazor.Color GetShareStatusColor(string? status)
    {
        return status switch
        {
            "Active" => MudBlazor.Color.Success,
            "Expired" => MudBlazor.Color.Warning,
            "Revoked" => MudBlazor.Color.Error,
            _ => MudBlazor.Color.Default
        };
    }

    /// <summary>
    /// Returns the resource key for a share status value, e.g. "Active" → "Active".
    /// Use with IStringLocalizer to display a localized status label.
    /// </summary>
    public static string GetShareStatusKey(string? status) => status switch
    {
        "Active" => "Active",
        "Expired" => "Expired",
        "Revoked" => "Revoked",
        _ => status ?? ""
    };

    // ===== ROLE DISPLAY =====

    /// <summary>
    /// Gets the MudBlazor color for a role (admin=error, manager=warning, contributor=info, viewer=default).
    /// </summary>
    public static MudBlazor.Color GetRoleColor(string? role)
    {
        return (role?.ToLowerInvariant()) switch
        {
            RoleHierarchy.Roles.Admin => MudBlazor.Color.Error,
            RoleHierarchy.Roles.Manager => MudBlazor.Color.Warning,
            RoleHierarchy.Roles.Contributor => MudBlazor.Color.Info,
            RoleHierarchy.Roles.Viewer => MudBlazor.Color.Default,
            _ => MudBlazor.Color.Default
        };
    }

    /// <summary>
    /// Returns the resource key for a role name, e.g. "viewer" → "Role_Viewer".
    /// Use with IStringLocalizer to display a localized role label.
    /// </summary>
    public static string GetRoleKey(string? role) => (role?.ToLowerInvariant()) switch
    {
        RoleHierarchy.Roles.Viewer => "Role_Viewer",
        RoleHierarchy.Roles.Contributor => "Role_Contributor",
        RoleHierarchy.Roles.Manager => "Role_Manager",
        RoleHierarchy.Roles.Admin => "Role_Admin",
        _ => role ?? ""
    };

    // ===== FORMATTING =====

    /// <summary>
    /// Formats a byte count into a human-readable file size string (e.g. "12.5 MB").
    /// Delegates to FileHelpers for the actual formatting.
    /// </summary>
    public static string FormatFileSize(long bytes) => FileHelpers.FormatFileSize(bytes);

    /// <summary>
    /// Converts a camelCase or PascalCase metadata key into readable "Title Case".
    /// Delegates to FileHelpers for the actual formatting.
    /// </summary>
    public static string FormatMetadataKey(string key) => FileHelpers.FormatMetadataKey(key);

    /// <summary>
    /// Checks whether the content type represents a PDF document.
    /// Delegates to FileHelpers for the actual check.
    /// </summary>
    public static bool IsPdfContentType(string? contentType) => FileHelpers.IsPdfContentType(contentType);
}
