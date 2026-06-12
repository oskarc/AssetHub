namespace AssetHub.Domain.Entities;

/// <summary>
/// Asset lifecycle status.
/// </summary>
public enum AssetStatus
{
    /// <summary>Fallback for unknown database values.</summary>
    Unknown = 0,
    Uploading,
    Processing,
    Ready,
    Failed
}

/// <summary>
/// Broad media type classification for an asset.
/// </summary>
public enum AssetType
{
    /// <summary>Fallback for unknown database values.</summary>
    Unknown = 0,
    Image,
    Video,
    Document,
    Audio
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the asset enums.
/// </summary>
// Each enum's ToDbString is kept next to its parser instead of grouping all
// ToDbString overloads together — easier to scan per-enum. Suppress S4136 for the file.
#pragma warning disable S4136
public static class AssetEnumExtensions
{
    private const string Unknown = "unknown";

    public static string ToDbString(this AssetStatus status) => status switch
    {
        AssetStatus.Uploading => "uploading",
        AssetStatus.Processing => "processing",
        AssetStatus.Ready => "ready",
        AssetStatus.Failed => "failed",
        AssetStatus.Unknown => Unknown,
        _ => Unknown // Fallback for future values
    };

    public static AssetStatus ToAssetStatus(this string value) => value switch
    {
        "uploading" => AssetStatus.Uploading,
        "processing" => AssetStatus.Processing,
        "ready" => AssetStatus.Ready,
        "failed" => AssetStatus.Failed,
        _ => AssetStatus.Unknown // Graceful fallback for unknown database values
    };

    public static string ToDbString(this AssetType type) => type switch
    {
        AssetType.Image => "image",
        AssetType.Video => "video",
        AssetType.Document => "document",
        AssetType.Audio => "audio",
        AssetType.Unknown => Unknown,
        _ => Unknown // Fallback for future values
    };

    public static AssetType ToAssetType(this string value) => value switch
    {
        "image" => AssetType.Image,
        "video" => AssetType.Video,
        "document" => AssetType.Document,
        "audio" => AssetType.Audio,
        _ => AssetType.Unknown // Graceful fallback for unknown database values
    };

    /// <summary>
    /// True when <paramref name="value"/> is a user-selectable asset type string.
    /// Deliberately excludes "unknown" (db fallback, never user input).
    /// </summary>
    public static bool IsValidAssetType(string value) => value is "image" or "video" or "document" or "audio";
}
#pragma warning restore S4136
