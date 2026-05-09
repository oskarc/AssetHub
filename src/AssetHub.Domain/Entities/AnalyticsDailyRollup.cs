namespace AssetHub.Domain.Entities;

/// <summary>
/// Per-day count rollup for usage / exposure metrics (T5-ANL-01). Each row
/// is a (date, metric, entity) tuple — e.g. "on 2026-05-01, asset X was
/// downloaded 12 times" or "on 2026-05-01, recipient hash H received 3
/// watermarked downloads".
///
/// Rows are upsert-idempotent: re-running the rollup for the same day for
/// the same entity overwrites the count. The rollup outlives its source —
/// once a day's audit / watermark events have been folded into a row, the
/// raw rows can be purged by audit retention without losing the analytic
/// view.
/// </summary>
public class AnalyticsDailyRollup
{
    /// <summary>UTC calendar day this rollup represents (no time component).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Which metric this row represents (downloads-by-asset, exposure-by-recipient, …).</summary>
    public AnalyticsCountMetric Metric { get; set; }

    /// <summary>
    /// Identifier for the entity this row aggregates over.
    /// - <see cref="AnalyticsCountMetric.DownloadsByAsset"/> → asset Guid as <c>D</c>-format hex.
    /// - <see cref="AnalyticsCountMetric.DownloadsByCollection"/> → collection Guid as <c>D</c>-format hex.
    /// - <see cref="AnalyticsCountMetric.ExposureByRecipient"/> → 64-char base64 of the recipient HMAC hash
    ///   (matches <c>WatermarkDownload.RecipientUserIdHash</c> / <c>RecipientEmailHash</c>; never plaintext PII).
    /// </summary>
    public string EntityId { get; set; } = "";

    /// <summary>Number of events for this (date, metric, entity) tuple.</summary>
    public long Count { get; set; }

    /// <summary>When the rollup row was last written (or upserted).</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Per-day byte-sum rollup for storage metrics (T5-ANL-01). Same shape as
/// <see cref="AnalyticsDailyRollup"/> but the value is bytes instead of count.
/// Storage rollup runs daily and stores the snapshot for that day so trend
/// charts work even after assets are deleted.
/// </summary>
public class AnalyticsStorageRollup
{
    /// <summary>UTC calendar day this snapshot represents.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Which metric this row represents (storage-by-collection, storage-by-asset-type).</summary>
    public AnalyticsStorageMetric Metric { get; set; }

    /// <summary>
    /// Identifier for the entity this row aggregates over.
    /// - <see cref="AnalyticsStorageMetric.StorageByCollection"/> → collection Guid as <c>D</c>-format hex.
    /// - <see cref="AnalyticsStorageMetric.StorageByAssetType"/> → asset-type string (<c>image</c>, <c>video</c>, …).
    /// </summary>
    public string EntityId { get; set; } = "";

    /// <summary>Sum of <c>Asset.SizeBytes</c> for the (date, metric, entity) bucket.</summary>
    public long Bytes { get; set; }

    /// <summary>Number of assets in the bucket on this day (denormalised for the chart-tooltip "X assets").</summary>
    public int AssetCount { get; set; }

    /// <summary>When the rollup row was last written.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Tracks an admin-requested PDF export of the analytics dashboard
/// (T5-ANL-01). Same lifecycle pattern as <see cref="ZipDownload"/> — the
/// API enqueues a Wolverine command, the worker renders + uploads to MinIO,
/// the UI polls until <see cref="Status"/> reaches
/// <see cref="AnalyticsPdfJobStatus.Ready"/> and then fetches a presigned URL.
/// </summary>
public class AnalyticsPdfJob
{
    public Guid Id { get; set; }

    /// <summary>Current job status.</summary>
    public AnalyticsPdfJobStatus Status { get; set; } = AnalyticsPdfJobStatus.Pending;

    /// <summary>Time-window the dashboard renders (matches the UI toggle: 7 / 30 / 90 / 365 days).</summary>
    public int WindowDays { get; set; }

    /// <summary>User ID that requested the export (for audit and per-user listing).</summary>
    public string RequestedByUserId { get; set; } = "";

    /// <summary>MinIO object key once the PDF is built; null while pending / failed.</summary>
    public string? ObjectKey { get; set; }

    /// <summary>Size of the rendered PDF in bytes; null while pending / failed.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Error message if the build failed; null on success.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>When the temporary PDF object should be deleted from MinIO (matches <c>ZipDownload.ExpiresAt</c> semantics).</summary>
    public DateTime ExpiresAt { get; set; }
}
