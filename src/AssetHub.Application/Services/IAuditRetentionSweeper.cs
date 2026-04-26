namespace AssetHub.Application.Services;

/// <summary>
/// Performs one pass of the audit-retention sweep — per-event-type overrides
/// first, then a single default-retention pass for everything else, then a
/// summary <c>audit.retention_purged</c> meta-event. Called from the
/// <c>AuditRetentionService</c> background worker on its configured cadence;
/// extracted so integration tests can drive a sweep directly without spinning
/// up the BackgroundService loop.
/// </summary>
public interface IAuditRetentionSweeper
{
    /// <summary>
    /// Runs one full sweep. Returns the total number of audit rows purged
    /// across all per-event-type passes plus the default pass.
    /// </summary>
    Task<int> SweepAsync(CancellationToken ct);
}
