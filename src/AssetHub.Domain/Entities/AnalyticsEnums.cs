namespace AssetHub.Domain.Entities;

/// <summary>
/// Count-style analytics rollup metric (T5-ANL-01). Each value defines a
/// distinct row family in <c>analytics_daily_rollup</c>; the rollup service
/// upserts one row per (date, metric, entity-id) tuple.
/// </summary>
public enum AnalyticsCountMetric
{
    /// <summary>One row per (day, asset) — count of <c>asset.downloaded</c> events.</summary>
    DownloadsByAsset,
    /// <summary>One row per (day, collection) — count of <c>collection.downloaded</c> + <c>collection.download_requested</c> events.</summary>
    DownloadsByCollection,
    /// <summary>One row per (day, recipient-hash) — count of <c>WatermarkDownload</c> rows grouped by HMAC hash. Stays opaque (G-12b carries forward from T5-WMK-01).</summary>
    ExposureByRecipient
}

/// <summary>
/// Byte-sum analytics rollup metric (T5-ANL-01). Distinct enum so the
/// storage rollup table can use a typed metric column without conflating
/// "counts" and "bytes" semantics.
/// </summary>
public enum AnalyticsStorageMetric
{
    /// <summary>One row per (day, collection) — sum of <c>Asset.SizeBytes</c> for assets currently in the collection.</summary>
    StorageByCollection,
    /// <summary>One row per (day, asset-type) — sum of <c>Asset.SizeBytes</c> grouped by <c>AssetType</c>.</summary>
    StorageByAssetType
}

/// <summary>
/// Lifecycle status of a queued analytics PDF export (T5-ANL-01).
/// </summary>
public enum AnalyticsPdfJobStatus
{
    /// <summary>Fallback for unknown database values.</summary>
    Unknown = 0,
    Pending,
    Building,
    Ready,
    Failed
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the analytics enums.
/// </summary>
// Each enum's ToDbString is kept next to its parser instead of grouping all
// ToDbString overloads together — easier to scan per-enum. Suppress S4136 for the file.
#pragma warning disable S4136
public static class AnalyticsEnumExtensions
{
    public static string ToDbString(this AnalyticsCountMetric metric) => metric switch
    {
        AnalyticsCountMetric.DownloadsByAsset => "downloads_by_asset",
        AnalyticsCountMetric.DownloadsByCollection => "downloads_by_collection",
        AnalyticsCountMetric.ExposureByRecipient => "exposure_by_recipient",
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };

    public static AnalyticsCountMetric ToAnalyticsCountMetric(this string value) => value switch
    {
        "downloads_by_asset" => AnalyticsCountMetric.DownloadsByAsset,
        "downloads_by_collection" => AnalyticsCountMetric.DownloadsByCollection,
        "exposure_by_recipient" => AnalyticsCountMetric.ExposureByRecipient,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown analytics count metric: {value}")
    };

    public static string ToDbString(this AnalyticsStorageMetric metric) => metric switch
    {
        AnalyticsStorageMetric.StorageByCollection => "storage_by_collection",
        AnalyticsStorageMetric.StorageByAssetType => "storage_by_asset_type",
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };

    public static AnalyticsStorageMetric ToAnalyticsStorageMetric(this string value) => value switch
    {
        "storage_by_collection" => AnalyticsStorageMetric.StorageByCollection,
        "storage_by_asset_type" => AnalyticsStorageMetric.StorageByAssetType,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown analytics storage metric: {value}")
    };

    public static string ToDbString(this AnalyticsPdfJobStatus status) => status switch
    {
        AnalyticsPdfJobStatus.Pending => "pending",
        AnalyticsPdfJobStatus.Building => "building",
        AnalyticsPdfJobStatus.Ready => "ready",
        AnalyticsPdfJobStatus.Failed => "failed",
        AnalyticsPdfJobStatus.Unknown => "unknown",
        _ => "unknown"
    };

    public static AnalyticsPdfJobStatus ToAnalyticsPdfJobStatus(this string value) => value switch
    {
        "pending" => AnalyticsPdfJobStatus.Pending,
        "building" => AnalyticsPdfJobStatus.Building,
        "ready" => AnalyticsPdfJobStatus.Ready,
        "failed" => AnalyticsPdfJobStatus.Failed,
        _ => AnalyticsPdfJobStatus.Unknown
    };
}
#pragma warning restore S4136
