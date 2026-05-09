using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Configuration for the analytics rollup pipeline (T5-ANL-01). Bound to
/// the "Analytics" section in appsettings; the <c>AnalyticsRollupBackgroundService</c>
/// reads these values once per tick, so config changes need a worker restart.
/// </summary>
/// <remarks>
/// <para>
/// The rollup runs once per UTC day at the configured hour/minute, then
/// every hour after that as a self-heal loop in case a previous tick was
/// missed (e.g. worker pod restarted at 02:00 UTC). The actual write
/// query is upsert-on-conflict so re-runs are idempotent.
/// </para>
/// <para>
/// On first start, the service back-fills <see cref="BackfillDays"/> worth
/// of history from the raw audit / watermark tables. Set this to 0 to
/// disable back-fill (only forward-rollup from now on).
/// </para>
/// </remarks>
public class AnalyticsSettings
{
    public const string SectionName = "Analytics";

    /// <summary>
    /// Master toggle. When <c>false</c>, the rollup background service starts but
    /// immediately returns each tick — the analytics page then shows an empty
    /// state with the configured-off message. Default is <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>UTC hour (0–23) at which the daily rollup is targeted to run. Default 02.</summary>
    [Range(0, 23)]
    public int CronHour { get; set; } = 2;

    /// <summary>UTC minute (0–59) at which the daily rollup is targeted to run. Default 00.</summary>
    [Range(0, 59)]
    public int CronMinute { get; set; }

    /// <summary>
    /// On first start, how many days of history to back-fill from the raw audit
    /// / watermark tables. Capped at 365 because longer windows increase the
    /// initial-startup query cost without paying for themselves on dashboards.
    /// Set to 0 to disable back-fill.
    /// </summary>
    [Range(0, 365)]
    public int BackfillDays { get; set; } = 90;

    /// <summary>
    /// How long a queued PDF export object lives in MinIO before
    /// <see cref="ZipCleanupBackgroundService"/>-style cleanup removes it.
    /// Mirrors the ZIP-download retention so the same operational expectations apply.
    /// </summary>
    [Range(1, 168)]
    public int PdfRetentionHours { get; set; } = 24;

    /// <summary>
    /// Top-N row cap on each panel. Higher values produce noisier dashboards
    /// without proportional value; lower values truncate long-tail visibility.
    /// </summary>
    [Range(5, 200)]
    public int TopN { get; set; } = 20;
}
