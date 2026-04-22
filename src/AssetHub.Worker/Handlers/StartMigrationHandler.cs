using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Handlers;

public sealed class StartMigrationHandler(
    IMigrationRepository migrationRepo,
    IAuditService audit,
    ILogger<StartMigrationHandler> logger)
{
    public async Task<object[]> HandleAsync(StartMigrationCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting migration {MigrationId}", command.MigrationId);

        var migration = await migrationRepo.GetByIdAsync(command.MigrationId, cancellationToken);
        if (migration is null)
        {
            logger.LogWarning("Migration {MigrationId} not found, skipping", command.MigrationId);
            return [];
        }

        if (migration.Status is not MigrationStatus.Running)
        {
            logger.LogWarning("Migration {MigrationId} is in {Status} status, not Running — skipping",
                command.MigrationId, migration.Status.ToDbString());
            return [];
        }

        var pendingItems = await migrationRepo.GetPendingItemsAsync(command.MigrationId, cancellationToken);

        // S3 migrations pull bytes from the remote bucket, so IsFileStaged never flips.
        // Local-file migrations (CSV) still require a staged file per item; unstaged
        // items stay pending and hold the migration in PartiallyCompleted.
        var itemsToDispatch = migration.SourceType is MigrationSourceType.S3
            ? pendingItems
            : pendingItems.Where(i => i.IsFileStaged).ToList();
        var skippedUnstaged = pendingItems.Count - itemsToDispatch.Count;

        if (skippedUnstaged > 0)
        {
            logger.LogInformation("Migration {MigrationId}: {UnstagedCount} pending items skipped (file not staged)",
                command.MigrationId, skippedUnstaged);
        }

        if (itemsToDispatch.Count == 0)
        {
            logger.LogInformation("Migration {MigrationId} has no dispatchable pending items", command.MigrationId);

            // Check if all items are terminal — finalize migration
            var counts = await migrationRepo.GetItemCountsAsync(command.MigrationId, cancellationToken);
            await FinalizeMigration(migration, counts, cancellationToken);
            return [];
        }

        logger.LogInformation("Migration {MigrationId}: fanning out {Count} item commands ({Total} pending, {Unstaged} unstaged)",
            command.MigrationId, itemsToDispatch.Count, pendingItems.Count, skippedUnstaged);

        var messages = new List<object>();
        foreach (var item in itemsToDispatch)
        {
            messages.Add(new ProcessMigrationItemCommand
            {
                MigrationId = command.MigrationId,
                MigrationItemId = item.Id
            });
        }

        return messages.ToArray();
    }

    private async Task FinalizeMigration(
        Migration migration, MigrationItemCounts counts, CancellationToken ct)
    {
        migration.ItemsSucceeded = counts.Succeeded;
        migration.ItemsFailed = counts.Failed;
        migration.ItemsSkipped = counts.Skipped;
        migration.FinishedAt = DateTime.UtcNow;

        migration.Status = ProcessMigrationItemHandler.ComputeTerminalStatus(migration, counts);

        await migrationRepo.UpdateAsync(migration, ct);

        await audit.LogAsync(
            MigrationConstants.AuditEvents.Completed,
            Constants.ScopeTypes.Migration,
            migration.Id,
            actorUserId: null,
            new Dictionary<string, object>
            {
                ["status"] = migration.Status.ToDbString(),
                ["succeeded"] = counts.Succeeded,
                ["failed"] = counts.Failed,
                ["skipped"] = counts.Skipped,
                ["total"] = counts.Total
            },
            ct);

        logger.LogInformation(
            "Migration {MigrationId} finalized: {Succeeded} succeeded, {Failed} failed, {Skipped} skipped",
            migration.Id, counts.Succeeded, counts.Failed, counts.Skipped);
    }
}
