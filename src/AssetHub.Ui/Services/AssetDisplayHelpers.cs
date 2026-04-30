using System.Collections.Concurrent;
using AssetHub.Application;
using AssetHub.Application.Helpers;
using Microsoft.Extensions.Localization;

namespace AssetHub.Ui.Services;

/// <summary>
/// Utility methods for displaying assets, shares, and roles in the UI.
/// Shared between all pages and components to eliminate duplication.
/// </summary>
public static class AssetDisplayHelpers
{
    // ===== THUMBNAILS & PLACEHOLDERS =====

    private static readonly ConcurrentDictionary<string, string> _placeholderCache = new();

    /// <summary>
    /// Gets the thumbnail URL for an asset, or generates a placeholder SVG.
    /// For videos, falls back to poster (first frame) when no thumbnail exists.
    /// </summary>
    public static string GetThumbnailUrl(Guid assetId, string? thumbObjectKey, string assetType, string? posterObjectKey = null)
    {
        if (!string.IsNullOrEmpty(thumbObjectKey))
        {
            return $"/api/v1/assets/{assetId}/thumb";
        }

        if (!string.IsNullOrEmpty(posterObjectKey))
        {
            return $"/api/v1/assets/{assetId}/poster";
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
    /// Results are cached since there are only a few asset types.
    /// </summary>
    public static string GetPlaceholderForType(string? assetType)
    {
        var key = assetType ?? "_default";
        return _placeholderCache.GetOrAdd(key, _ => assetType switch
        {
            Constants.AssetTypeFilters.Image => GetPlaceholderSvg("Image", "#4CAF50", "M21,19V5c0-1.1-0.9-2-2-2H5C3.9,3,3,3.9,3,5v14c0,1.1,0.9,2,2,2h14C20.1,21,21,20.1,21,19z M8.5,13.5l2.5,3.01L14.5,12l4.5,6H5L8.5,13.5z"),
            Constants.AssetTypeFilters.Video => GetPlaceholderSvg("Video", "#2196F3", "M17,10.5V7c0-0.55-0.45-1-1-1H4C3.45,6,3,6.45,3,7v10c0,0.55,0.45,1,1,1h12c0.55,0,1-0.45,1-1v-3.5l4,4v-11L17,10.5z"),
            Constants.AssetTypeFilters.Document => GetPlaceholderSvg("Document", "#FF9800", "M14,2H6C4.9,2,4.01,2.9,4.01,4L4,20c0,1.1,0.89,2,1.99,2H18c1.1,0,2-0.9,2-2V8L14,2z M16,18H8v-2h8V18z M16,14H8v-2h8V14z M13,9V3.5L18.5,9H13z"),
            _ => GetPlaceholderSvg("Asset", "#9E9E9E", "M6,2C4.89,2,4,2.9,4,4v16c0,1.1,0.89,2,2,2h12c1.1,0,2-0.9,2-2V8l-6-6H6z M13,9V3.5L18.5,9H13z")
        });
    }

    // ===== ASSET TYPE DISPLAY =====

    /// <summary>
    /// Gets the MudBlazor color for an asset type.
    /// </summary>
    public static MudBlazor.Color GetAssetTypeColor(string? assetType)
    {
        return assetType switch
        {
            Constants.AssetTypeFilters.Image => MudBlazor.Color.Success,
            Constants.AssetTypeFilters.Video => MudBlazor.Color.Info,
            Constants.AssetTypeFilters.Document => MudBlazor.Color.Warning,
            Constants.AssetTypeFilters.Audio => MudBlazor.Color.Secondary,
            _ => MudBlazor.Color.Default
        };
    }

    /// <summary>
    /// Returns the CommonResource key for an asset type, e.g. "image" → "AssetType_Image".
    /// Use with IStringLocalizer to display a localized type label.
    /// </summary>
    public static string GetAssetTypeKey(string? assetType) => (assetType?.ToLowerInvariant()) switch
    {
        Constants.AssetTypeFilters.Image => "AssetType_Image",
        Constants.AssetTypeFilters.Video => "AssetType_Video",
        Constants.AssetTypeFilters.Document => "AssetType_Document",
        Constants.AssetTypeFilters.Audio => "AssetType_Audio",
        _ => assetType ?? ""
    };

    /// <summary>
    /// Gets the MudBlazor icon string for an asset type.
    /// </summary>
    public static string GetAssetIcon(string? assetType)
    {
        return assetType switch
        {
            Constants.AssetTypeFilters.Image => MudBlazor.Icons.Material.Filled.Image,
            Constants.AssetTypeFilters.Video => MudBlazor.Icons.Material.Filled.VideoFile,
            Constants.AssetTypeFilters.Document => MudBlazor.Icons.Material.Filled.Description,
            Constants.AssetTypeFilters.Audio => MudBlazor.Icons.Material.Filled.AudioFile,
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
        if (contentType.StartsWith("audio/"))
            return MudBlazor.Icons.Material.Filled.AudioFile;
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
    /// Returns the CommonResource key for an asset processing status, e.g. "ready" → "AssetStatus_Ready".
    /// Use with IStringLocalizer to display a localized status label.
    /// </summary>
    public static string GetAssetStatusKey(string? status) => (status?.ToLowerInvariant()) switch
    {
        "ready" => "AssetStatus_Ready",
        "processing" => "AssetStatus_Processing",
        "failed" => "AssetStatus_Failed",
        _ => status ?? ""
    };

    /// <summary>
    /// Returns a MudBlazor icon for an asset processing status, so that status is not conveyed
    /// by color alone (WCAG 1.4.1).
    /// </summary>
    public static string GetAssetStatusIcon(string? status) => (status?.ToLowerInvariant()) switch
    {
        "ready" => MudBlazor.Icons.Material.Filled.CheckCircle,
        "processing" => MudBlazor.Icons.Material.Filled.HourglassTop,
        "failed" => MudBlazor.Icons.Material.Filled.Error,
        _ => MudBlazor.Icons.Material.Filled.Circle
    };

    /// <summary>
    /// Gets the MudBlazor color for a share status (Active/Expired/Revoked).
    /// </summary>
    public static MudBlazor.Color GetShareStatusColor(string? status)
    {
        return status switch
        {
            Constants.ShareStatus.Active => MudBlazor.Color.Success,
            Constants.ShareStatus.Expired => MudBlazor.Color.Warning,
            Constants.ShareStatus.Revoked => MudBlazor.Color.Error,
            _ => MudBlazor.Color.Default
        };
    }

    /// <summary>
    /// Returns the resource key for a share status value, e.g. "Active" → "Active".
    /// Use with IStringLocalizer to display a localized status label.
    /// </summary>
    public static string GetShareStatusKey(string? status) => status switch
    {
        Constants.ShareStatus.Active => Constants.ShareStatus.Active,
        Constants.ShareStatus.Expired => Constants.ShareStatus.Expired,
        Constants.ShareStatus.Revoked => Constants.ShareStatus.Revoked,
        _ => status ?? ""
    };

    // ===== AUDIT EVENT DISPLAY =====

    // Fragment-to-color rules in lookup-priority order (first match wins).
    private static readonly (string[] Fragments, MudBlazor.Color Color)[] AuditColorRules =
    {
        (new[] { "malware", "processing_failed", "password_failed" }, MudBlazor.Color.Error),
        (new[] { "delete", "revoke", "removed", "cleanup" }, MudBlazor.Color.Error),
        (new[] { "create", "upload" }, MudBlazor.Color.Success),
        (new[] { "update" }, MudBlazor.Color.Warning),
        (new[] { "download", "share" }, MudBlazor.Color.Info),
        (new[] { "access", "acl" }, MudBlazor.Color.Secondary),
    };

    /// <summary>
    /// Gets the MudBlazor color for an audit event type.
    /// Used by both the dashboard and admin audit tab.
    /// </summary>
    public static MudBlazor.Color GetAuditEventColor(string eventType)
    {
        var lowered = eventType.ToLowerInvariant();
        var match = AuditColorRules.FirstOrDefault(rule => rule.Fragments.Any(lowered.Contains));
        return match.Fragments is null ? MudBlazor.Color.Default : match.Color;
    }

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

    // ===== CONTENT TYPE DISPLAY =====

    private static readonly Dictionary<string, string> ContentTypeKeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "ContentType_JPEG",
        ["image/jpg"] = "ContentType_JPEG",
        ["image/png"] = "ContentType_PNG",
        ["image/gif"] = "ContentType_GIF",
        ["image/webp"] = "ContentType_WebP",
        ["image/svg+xml"] = "ContentType_SVG",
        ["image/tiff"] = "ContentType_TIFF",
        ["image/bmp"] = "ContentType_BMP",
        ["video/mp4"] = "ContentType_MP4",
        ["video/webm"] = "ContentType_WebM",
        ["video/quicktime"] = "ContentType_MOV",
        ["video/x-msvideo"] = "ContentType_AVI",
        ["application/pdf"] = "ContentType_PDF",
        ["application/msword"] = "ContentType_Word",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "ContentType_Word",
        ["application/vnd.ms-excel"] = "ContentType_Excel",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "ContentType_Excel",
        ["application/vnd.ms-powerpoint"] = "ContentType_PowerPoint",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "ContentType_PowerPoint",
        ["application/zip"] = "ContentType_ZIP",
        ["application/x-zip-compressed"] = "ContentType_ZIP",
        ["audio/mpeg"] = "ContentType_MP3",
        ["audio/mp3"] = "ContentType_MP3",
        ["audio/wav"] = "ContentType_WAV",
        ["audio/x-wav"] = "ContentType_WAV",
        ["text/plain"] = "ContentType_PlainText",
    };

    /// <summary>
    /// Returns the CommonResource key for a MIME content type, e.g. "image/jpeg" → "ContentType_JPEG".
    /// Falls back to an empty string if no mapping exists.
    /// </summary>
    public static string GetContentTypeKey(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return "";
        return ContentTypeKeyMap.TryGetValue(contentType, out var key) ? key : "";
    }

    // ===== FORMATTING =====

    /// <summary>
    /// Formats a byte count into a human-readable file size string (e.g. "12.5 MB").
    /// Delegates to FileHelpers for the actual formatting.
    /// </summary>
    public static string FormatFileSize(long bytes) => FileHelpers.FormatFileSize(bytes);

    /// <summary>
    /// Formats a duration in seconds as <c>m:ss</c> for clips under an hour and
    /// <c>h:mm:ss</c> for longer ones. Audio assets and (later) videos use this
    /// for the file-info row in the detail page.
    /// </summary>
    public static string FormatDuration(int seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

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

    // ===== LOCALIZED DISPLAY HELPERS =====

    /// <summary>
    /// Returns the localized display name for a role, falling back to the raw value.
    /// </summary>
    public static string GetLocalizedRole(string? role, IStringLocalizer loc)
    {
        var key = GetRoleKey(role);
        if (string.IsNullOrEmpty(key)) return role ?? "";
        var localized = loc[key];
        return localized.ResourceNotFound ? role ?? "" : localized.Value;
    }

    /// <summary>
    /// Returns the localized display name for an asset type, falling back to the raw value.
    /// </summary>
    public static string GetLocalizedAssetType(string? assetType, IStringLocalizer loc)
    {
        var key = GetAssetTypeKey(assetType);
        if (string.IsNullOrEmpty(key)) return assetType ?? "";
        var localized = loc[key];
        return localized.ResourceNotFound ? assetType ?? "" : localized.Value;
    }

    /// <summary>
    /// Returns the localized display name for a content type, falling back to the raw MIME type.
    /// </summary>
    public static string GetLocalizedContentType(string? contentType, IStringLocalizer loc)
    {
        var key = GetContentTypeKey(contentType);
        if (string.IsNullOrEmpty(key)) return contentType ?? "";
        var localized = loc[key];
        return localized.ResourceNotFound ? contentType ?? "" : localized.Value;
    }

    /// <summary>
    /// Returns the localized display name for a scope type (asset, collection, etc.), falling back to the raw value.
    /// </summary>
    public static string GetLocalizedScopeType(string? scopeType, IStringLocalizer loc)
    {
        if (string.IsNullOrEmpty(scopeType)) return scopeType ?? "";
        var key = $"ScopeType_{char.ToUpper(scopeType[0])}{scopeType[1..]}";
        var localized = loc[key];
        return localized.ResourceNotFound ? scopeType : localized.Value;
    }
}
