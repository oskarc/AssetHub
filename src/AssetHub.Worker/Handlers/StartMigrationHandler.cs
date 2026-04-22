using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Handlers;

public sealed class StartMigrationHandler(
    IMigrationRepository migrationRepo,
    IMigrationSourceConnectorRegistry connectors,
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

        var connector = connectors.Resolve(migration.SourceType);
        var pendingItems = await migrationRepo.GetPendingItemsAsync(command.MigrationId, cancellationToken);

        // Staging-based sources (CSV) only dispatch items whose bytes have been
        // uploaded to the staging bucket — unstaged items stay pending and hold
        // the migration in PartiallyCompleted. Remote-pull sources fan out every
        // pending item.
        var itemsToDispatch = connector.RequiresLocalStaging
            ? pendingItems.Where(i => i.IsFileStaged).ToList()
            : pendingItems;
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
            await FinalizeMigration(migration, connector, counts, cancellationToken);
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
        Migration migration, IMigrationSourceConnector connector, MigrationItemCounts counts, CancellationToken ct)
    {
        migration.ItemsSucceeded = counts.Succeeded;
        migration.ItemsFailed = counts.Failed;
        migration.ItemsSkipped = counts.Skipped;
        migration.FinishedAt = DateTime.UtcNow;

        migration.Status = ProcessMigrationItemHandler.ComputeTerminalStatus(connector, counts);

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
