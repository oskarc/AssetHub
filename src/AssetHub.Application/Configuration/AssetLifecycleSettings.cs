using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Lifecycle rules for assets: soft-delete retention before a background worker purges
/// rows permanently. Bound to the "AssetLifecycle" section in appsettings.
/// </summary>
public class AssetLifecycleSettings
{
    public const string SectionName = "AssetLifecycle";

    /// <summary>
    /// Days a soft-deleted asset stays in Trash before the purge worker removes it and its
    /// MinIO objects. Default 30 is the enterprise-DAM norm. Tests drop this to seconds via
    /// configuration overrides to exercise the full purge loop in reasonable time.
    /// </summary>
    [Range(0, 3650)]
    public int TrashRetentionDays { get; set; } = 30;

    /// <summary>
    /// How often the purge worker scans for expired trash rows. Shorter intervals react
    /// faster but add constant DB pressure; 1 hour is a good balance for admin UX.
    /// </summary>
    [Range(1, 1440)]
    public int PurgeIntervalMinutes { get; set; } = 60;
}
