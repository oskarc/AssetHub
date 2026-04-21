namespace AssetHub.Application.Messages;

// ── Commands ─────────────────────────────────────────────────────────────

/// <summary>
/// Starts processing a migration job. The handler fans out
/// individual ProcessMigrationItemCommand messages for each pending item.
/// </summary>
public record StartMigrationCommand
{
    public Guid MigrationId { get; init; }
}

/// <summary>
/// Processes a single migration item — uploads the file and creates the asset.
/// </summary>
public record ProcessMigrationItemCommand
{
    public Guid MigrationId { get; init; }
    public Guid MigrationItemId { get; init; }
}

/// <summary>
/// Enumerates objects in the S3 bucket configured on the migration and creates
/// a <c>MigrationItem</c> row per object. Runs in the worker so a large bucket
/// scan doesn't block the admin request thread.
/// </summary>
public record S3MigrationScanCommand
{
    public Guid MigrationId { get; init; }
}

// ── Events ───────────────────────────────────────────────────────────────

/// <summary>
/// Published when all items in a migration have been processed (success or failure).
/// </summary>
public record MigrationCompletedEvent
{
    public Guid MigrationId { get; init; }
    public int ItemsSucceeded { get; init; }
    public int ItemsFailed { get; init; }
    public int ItemsSkipped { get; init; }
}
