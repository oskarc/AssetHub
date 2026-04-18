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
