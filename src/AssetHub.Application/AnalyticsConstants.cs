namespace AssetHub.Application;

/// <summary>
/// Feature-specific constants for analytics + reporting (T5-ANL-01).
/// Mirrors the layout of <see cref="WatermarkConstants"/>.
/// </summary>
public static class AnalyticsConstants
{
    /// <summary>Audit event-type strings emitted by the analytics pipeline.</summary>
    public static class AuditEvents
    {
        /// <summary>
        /// An admin downloaded a CSV or PDF export. Records the panel + window +
        /// row count in <c>Details</c>; never the actual rows.
        /// </summary>
        public const string Exported = "analytics.exported";

        /// <summary>
        /// An admin clicked "reveal recipient" on the exposure panel. Records the
        /// recipient hash + window + count only — never the decrypted plaintext
        /// (G-12b carries forward from T5-WMK-01).
        /// </summary>
        public const string ExposureRevealed = "analytics.exposure_revealed";

        /// <summary>
        /// One row per successful daily rollup tick, summarising what was written.
        /// Lets retention auditors verify the rollup outlived the source events.
        /// </summary>
        public const string RollupCompleted = "analytics.rollup_completed";
    }

    /// <summary>Scope types used in the analytics audit-row <c>TargetType</c> column.</summary>
    public static class ScopeTypes
    {
        /// <summary>Used for <c>analytics.exported</c> + <c>analytics.exposure_revealed</c> rows.</summary>
        public const string Analytics = "analytics";
    }

    /// <summary>Recipient-kind discriminator for the exposure panel.</summary>
    public static class RecipientKinds
    {
        public const string User = "user";
        public const string Email = "email";
    }

    /// <summary>MinIO object-key prefix for queued analytics PDF exports.</summary>
    public const string PdfObjectKeyPrefix = "analytics-pdfs/";
}
