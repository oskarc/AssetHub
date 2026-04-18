using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Handlers;

public sealed class StartMigrationHandler(
    IMigrationRepository migrationRepo,
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

        // Only process items that have their file staged — unstaged items stay pending
        var stagedItems = pendingItems.Where(i => i.IsFileStaged).ToList();
        var unstagedCount = pendingItems.Count - stagedItems.Count;

        if (unstagedCount > 0)
        {
            logger.LogInformation("Migration {MigrationId}: {UnstagedCount} pending items skipped (file not staged)",
                command.MigrationId, unstagedCount);
        }

        if (stagedItems.Count == 0)
        {
            logger.LogInformation("Migration {MigrationId} has no staged pending items", command.MigrationId);

            // Check if all staged items are terminal — finalize migration
            var counts = await migrationRepo.GetItemCountsAsync(command.MigrationId, cancellationToken);
            await FinalizeMigration(migration, counts, cancellationToken);
            return [];
        }

        logger.LogInformation("Migration {MigrationId}: fanning out {Count} item commands ({Total} pending, {Unstaged} unstaged)",
            command.MigrationId, stagedItems.Count, pendingItems.Count, unstagedCount);

        // Fan out individual item commands for staged items only
        var messages = new List<object>();
        foreach (var item in stagedItems)
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

        migration.Status = counts.Failed > 0
            ? MigrationStatus.CompletedWithErrors
            : counts.Staged < counts.Total
                ? MigrationStatus.PartiallyCompleted
                : MigrationStatus.Completed;

        await migrationRepo.UpdateAsync(migration, ct);

        logger.LogInformation(
            "Migration {MigrationId} finalized: {Succeeded} succeeded, {Failed} failed, {Skipped} skipped",
            migration.Id, counts.Succeeded, counts.Failed, counts.Skipped);
    }
}
