using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Retention policy for the <c>AuditEvents</c> table. Bound to the "AuditRetention"
/// section in appsettings; the <c>AuditRetentionService</c> background worker reads
/// these values once per sweep, so config changes need a worker restart to apply.
/// </summary>
/// <remarks>
/// <para>
/// Without retention, audit rows accumulate forever — fine for low-volume security
/// trails (ACL grants, PAT mint/revoke) but ruinous for high-volume telemetry-grade
/// events (downloads, webhook deliveries, share accesses). The per-event-type
/// override map is the lever: keep regulation-relevant trails for the default
/// two years, shrink the chatty ones to a quarter or so.
/// </para>
/// <para>
/// SOC2 / ISO 27001 audits typically expect at least 12 months of relevant audit
/// trail; the default 730 days lines up with the typical 2-year evidence window.
/// Per-event overrides should only ever shrink retention for genuinely high-volume
/// events that would otherwise overwhelm the table — never use overrides to cut
/// security-relevant trails (ACL changes, PAT lifecycle, share password failures).
/// </para>
/// </remarks>
public class AuditRetentionSettings
{
    public const string SectionName = "AuditRetention";

    /// <summary>
    /// Days a row stays in <c>AuditEvents</c> before the sweeper deletes it,
    /// for any event type not listed in <see cref="PerEventTypeOverrides"/>.
    /// Default 730 (~2 years) aligns with typical SOC2 evidence windows.
    /// </summary>
    [Range(1, 3650)]
    public int DefaultRetentionDays { get; set; } = 730;

    /// <summary>
    /// Per-event-type retention overrides. Maps an event type string
    /// (e.g. <c>"asset.downloaded"</c>) to its retention in days.
    /// Anything not listed here uses <see cref="DefaultRetentionDays"/>.
    /// </summary>
    public Dictionary<string, int> PerEventTypeOverrides { get; set; } = new();

    /// <summary>
    /// How often the sweeper runs. One hour is a reasonable balance — frequent
    /// enough that the table doesn't grow noticeably between sweeps under
    /// normal load, infrequent enough to avoid constant DB pressure.
    /// </summary>
    [Range(60, 86400)]
    public int SweepIntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// Maximum rows deleted per <c>ExecuteDeleteAsync</c> call. Caps the
    /// transaction size on high-traffic tables so a single sweep can't
    /// spike replication lag. The sweeper loops until a batch returns
    /// fewer rows than the limit.
    /// </summary>
    [Range(100, 100_000)]
    public int BatchSize { get; set; } = 5000;
}
