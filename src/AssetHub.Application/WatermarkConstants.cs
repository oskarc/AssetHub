namespace AssetHub.Application;

/// <summary>
/// Feature-specific constants for forensic watermarking (T5-WMK-01).
/// Cross-cutting constants (DataProtection purposes, ScopeTypes, RateLimitPolicies)
/// stay in <see cref="Constants"/>.
/// </summary>
public static class WatermarkConstants
{
    /// <summary>Audit event-type strings emitted by the watermarking pipeline.</summary>
    public static class AuditEvents
    {
        /// <summary>Background sweep embedded the asset-fingerprint into stored renditions.</summary>
        public const string Embedded = "watermark.embedded";

        /// <summary>
        /// Per-download event written when a watermarked file is served.
        /// High-volume — retention is reduced via <c>AuditRetentionSettings.PerEventOverrides</c>
        /// (default 90 days).
        /// </summary>
        public const string AppliedOnDownload = "watermark.applied_on_download";

        /// <summary>
        /// Admin verify endpoint hit. Records <c>(assetId, downloadToken)</c> only —
        /// never the resolved recipient PII.
        /// </summary>
        public const string Verified = "watermark.verified";

        /// <summary>Collection-level <c>WatermarkEnabled</c> toggled.</summary>
        public const string CollectionEnabled = "watermark.collection_enabled";

        /// <summary>Asset-level <c>WatermarkOverride</c> changed.</summary>
        public const string AssetOverridden = "watermark.asset_overridden";

        /// <summary>Share-level <c>WatermarkOverride</c> changed.</summary>
        public const string ShareOverridden = "watermark.share_overridden";
    }

    /// <summary>
    /// Algorithm-version identifiers stamped onto every <c>WatermarkDownload</c> row
    /// so future rotations can decode legacy rows.
    /// </summary>
    public static class AlgorithmVersions
    {
        /// <summary>v1: DCT-domain LSB modulation, single-key.</summary>
        public const string V1 = "v1";
    }

    /// <summary>
    /// Service-result error codes returned by the watermarking surface.
    /// Map to HTTP via <c>ServiceResult.ToHttpResult()</c>.
    /// </summary>
    public static class ErrorCodes
    {
        /// <summary>The verify endpoint extracted a token but no <c>WatermarkDownload</c> row matches it.</summary>
        public const string TokenNotFound = "WMK_TOKEN_NOT_FOUND";

        /// <summary>The uploaded verify file isn't an image format we can extract from.</summary>
        public const string UnsupportedFormat = "WMK_UNSUPPORTED_FORMAT";

        /// <summary>The uploaded verify file exceeds <c>WatermarkSettings.VerifyMaxBytes</c>.</summary>
        public const string TooLarge = "WMK_TOO_LARGE";

        /// <summary>Watermark embed or extract failed (wrapped infrastructure exception).</summary>
        public const string ExtractionFailed = "WMK_EXTRACTION_FAILED";

        /// <summary>DataProtection failed to decrypt a row's PII columns (likely key-rotation drift).</summary>
        public const string DecryptionFailed = "WMK_DECRYPTION_FAILED";
    }
}
