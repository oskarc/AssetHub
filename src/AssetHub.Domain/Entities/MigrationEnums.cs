namespace AssetHub.Domain.Entities;

/// <summary>
/// Source type for a bulk import migration.
/// </summary>
public enum MigrationSourceType
{
    CsvUpload,
    S3
}

/// <summary>
/// Overall status of a migration job.
/// </summary>
public enum MigrationStatus
{
    Draft,
    Validating,
    Running,
    Completed,
    PartiallyCompleted,
    CompletedWithErrors,
    Failed,
    Cancelled
}

/// <summary>
/// Status of an individual migration item.
/// </summary>
public enum MigrationItemStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Skipped
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the migration enums.
/// </summary>
// Each enum's ToDbString is kept next to its parser instead of grouping all
// ToDbString overloads together — easier to scan per-enum. Suppress S4136 for the file.
#pragma warning disable S4136
public static class MigrationEnumExtensions
{
    private const string Failed = "failed";

    public static string ToDbString(this MigrationSourceType type) => type switch
    {
        MigrationSourceType.CsvUpload => "csv_upload",
        MigrationSourceType.S3 => "s3",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static MigrationSourceType ToMigrationSourceType(this string value) => value switch
    {
        "csv_upload" => MigrationSourceType.CsvUpload,
        "s3" => MigrationSourceType.S3,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown migration source type: {value}")
    };

    public static string ToDbString(this MigrationStatus status) => status switch
    {
        MigrationStatus.Draft => "draft",
        MigrationStatus.Validating => "validating",
        MigrationStatus.Running => "running",
        MigrationStatus.Completed => "completed",
        MigrationStatus.PartiallyCompleted => "partially_completed",
        MigrationStatus.CompletedWithErrors => "completed_with_errors",
        MigrationStatus.Failed => Failed,
        MigrationStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static MigrationStatus ToMigrationStatus(this string value) => value switch
    {
        "draft" => MigrationStatus.Draft,
        "validating" => MigrationStatus.Validating,
        "running" => MigrationStatus.Running,
        "completed" => MigrationStatus.Completed,
        "partially_completed" => MigrationStatus.PartiallyCompleted,
        "completed_with_errors" => MigrationStatus.CompletedWithErrors,
        Failed => MigrationStatus.Failed,
        "cancelled" => MigrationStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown migration status: {value}")
    };

    public static string ToDbString(this MigrationItemStatus status) => status switch
    {
        MigrationItemStatus.Pending => "pending",
        MigrationItemStatus.Processing => "processing",
        MigrationItemStatus.Succeeded => "succeeded",
        MigrationItemStatus.Failed => Failed,
        MigrationItemStatus.Skipped => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static MigrationItemStatus ToMigrationItemStatus(this string value) => value switch
    {
        "pending" => MigrationItemStatus.Pending,
        "processing" => MigrationItemStatus.Processing,
        "succeeded" => MigrationItemStatus.Succeeded,
        Failed => MigrationItemStatus.Failed,
        "skipped" => MigrationItemStatus.Skipped,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown migration item status: {value}")
    };
}
#pragma warning restore S4136
