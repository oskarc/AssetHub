using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Forensic watermarking configuration (T5-WMK-01). Bound to the "Watermarking" section
/// and validated on start because misconfigured intervals cause silent functional drift —
/// e.g. a sweep interval of zero would never run; a zero size limit would force every
/// download into async-202.
/// </summary>
public class WatermarkSettings
{
    public const string SectionName = "Watermarking";

    /// <summary>
    /// How often the asset-fingerprint background sweep ticks. The sweep is purely a
    /// performance optimisation — the no-gap rule means downloads embed both layers
    /// on-the-fly when the asset's stored renditions aren't pre-baked. So frequency
    /// trades CPU against the extra on-the-fly latency for newly-flagged assets.
    /// Range: 1 minute to 24 hours.
    /// </summary>
    [Range(60, 86400)]
    public int BackgroundSweepIntervalSeconds { get; set; } = 6 * 60 * 60;

    /// <summary>
    /// Megapixel ceiling for synchronous on-the-fly recipient-layer (and asset-layer
    /// when needed) embedding. Above this, the download endpoint returns 202 +
    /// Retry-After, enqueues a <c>RecipientWatermarkCommand</c>, and the client polls
    /// for completion. 50 MP catches almost every typical brand asset; gigapixel
    /// originals fall to async.
    /// </summary>
    [Range(1, 1000)]
    public int SyncSizeLimitMP { get; set; } = 50;

    /// <summary>
    /// Maximum upload size accepted by the admin verify endpoint. 50 MB matches typical
    /// JPEG / PNG sizes a leaker might forward; anything larger is almost certainly
    /// not a legitimately-leaked image.
    /// </summary>
    [Range(1024, int.MaxValue)]
    public int VerifyMaxBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Per-tick batch size for the asset-fingerprint sweep. Each batch is enqueued as
    /// individual <c>EmbedAssetFingerprintCommand</c> messages; Wolverine fan-out then
    /// processes them in parallel. Tighter batches keep DB scan cheap; wider batches
    /// reduce per-batch overhead.
    /// </summary>
    [Range(1, 1000)]
    public int SweepBatchSize { get; set; } = 100;

    /// <summary>
    /// Base64-encoded HMAC key (32 bytes / 256 bits). Used by
    /// <c>IRecipientCryptoService</c> for two things: embedded watermark
    /// tamper-detection, and the indexable HMAC hash columns in
    /// <c>WatermarkDownloads</c>. Generate once per deployment with
    /// <c>openssl rand -base64 32</c> (or equivalent) and set via env var
    /// <c>Watermarking__HmacKeyBase64</c> / Docker secret. Rotation is destructive
    /// in v1 — re-watermarking existing assets would be required (tracked as a
    /// FOLLOW-UPS item).
    /// </summary>
    [Required]
    [StringLength(64, MinimumLength = 44)]
    public string HmacKeyBase64 { get; set; } = string.Empty;
}
