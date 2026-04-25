using System.Security.Cryptography;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.Handlers;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Migration item handler is the convergence point for source connector + asset/collection repos + MinIO + media processing + migration row updates. The whole point is per-item ingest orchestration.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Same justification as S1200 above — Wolverine handler ctor wires together every collaborator needed for one migration item; bundling them into a holder would just relocate the parameter count.")]
public sealed class ProcessMigrationItemHandler(
    IMigrationRepository migrationRepo,
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    ICollectionRepository collectionRepo,
    ICollectionAclRepository collectionAclRepo,
    IMinIOAdapter minioAdapter,
    IMigrationSourceConnectorRegistry connectors,
    IMediaProcessingService mediaProcessing,
    IAuditService audit,
    HybridCache cache,
    IOptions<MinIOSettings> minioSettings,
    ILogger<ProcessMigrationItemHandler> logger)
{
    private readonly string _bucketName = minioSettings.Value.BucketName;

    public async Task HandleAsync(ProcessMigrationItemCommand command, CancellationToken cancellationToken)
    {
        var item = await migrationRepo.GetItemByIdAsync(command.MigrationItemId, cancellationToken);
        if (item is null)
        {
            logger.LogWarning("Migration item {ItemId} not found, skipping", command.MigrationItemId);
            return;
        }

        // Skip if already terminal
        if (item.Status is MigrationItemStatus.Succeeded or MigrationItemStatus.Failed or MigrationItemStatus.Skipped)
        {
            logger.LogDebug("Migration item {ItemId} is already in terminal status {Status}",
                command.MigrationItemId, item.Status.ToDbString());
            return;
        }

        var migration = await migrationRepo.GetByIdAsync(command.MigrationId, cancellationToken);
        if (migration is null || migration.Status is MigrationStatus.Cancelled)
        {
            logger.LogWarning("Migration {MigrationId} not found or cancelled, skipping item {ItemId}",
                command.MigrationId, command.MigrationItemId);
            item.Status = MigrationItemStatus.Skipped;
            item.ErrorCode = MigrationConstants.ErrorCodes.MigrationCancelled;
            item.ErrorMessage = "Migration was cancelled.";
            item.ProcessedAt = DateTime.UtcNow;
            await migrationRepo.UpdateItemAsync(item, cancellationToken);
            return;
        }

        item.Status = MigrationItemStatus.Processing;
        item.AttemptCount++;
        await migrationRepo.UpdateItemAsync(item, cancellationToken);

        try
        {
            if (migration.DryRun)
            {
                await ProcessDryRunAsync(item, cancellationToken);
            }
            else
            {
                var connector = connectors.Resolve(migration.SourceType);
                await ProcessItemAsync(migration, item, connector, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration item {ItemId} failed: {Error}", item.Id, ex.Message);
            item.Status = MigrationItemStatus.Failed;
            item.ErrorCode = MigrationConstants.ErrorCodes.ProcessingError;
            item.ErrorMessage = ex.Message.Length > MigrationConstants.Limits.MaxErrorMessageLength
                ? ex.Message[..MigrationConstants.Limits.MaxErrorMessageLength]
                : ex.Message;
            item.ProcessedAt = DateTime.UtcNow;
            await migrationRepo.UpdateItemAsync(item, cancellationToken);
        }
        finally
        {
            // Check if all items are terminal — if so, finalize migration
            await TryFinalizeMigration(command.MigrationId, cancellationToken);
        }
    }

    private async Task ProcessDryRunAsync(MigrationItem item, CancellationToken ct)
    {
        // Validate basic fields
        if (string.IsNullOrWhiteSpace(item.FileName))
        {
            item.Status = MigrationItemStatus.Failed;
            item.ErrorCode = MigrationConstants.ErrorCodes.MissingFilename;
            item.ErrorMessage = "Filename is required.";
            item.ProcessedAt = DateTime.UtcNow;
            await migrationRepo.UpdateItemAsync(item, ct);
            return;
        }

        // Check for duplicate by SHA256 across existing assets
        if (!string.IsNullOrWhiteSpace(item.Sha256))
        {
            var existing = await assetRepo.GetBySha256Async(item.Sha256, ct);
            if (existing is not null)
            {
                item.Status = MigrationItemStatus.Skipped;
                item.ErrorCode = MigrationConstants.ErrorCodes.Duplicate;
                item.ErrorMessage = $"Asset with SHA256 {item.Sha256} already exists (Asset ID: {existing.Id}).";
                item.ProcessedAt = DateTime.UtcNow;
                await migrationRepo.UpdateItemAsync(item, ct);
                return;
            }
        }

        // Dry run — mark as succeeded (validation passed)
        item.Status = MigrationItemStatus.Succeeded;
        item.ProcessedAt = DateTime.UtcNow;
        await migrationRepo.UpdateItemAsync(item, ct);
    }

    /// <summary>
    /// Source-agnostic ingest: stat + download via the connector, hash + dup-check,
    /// then write through the internal MinIO adapter and persist the asset.
    /// </summary>
    private async Task ProcessItemAsync(
        Migration migration, MigrationItem item, IMigrationSourceConnector connector, CancellationToken ct)
    {
        var sourceKey = connector.ResolveSourceKey(migration, item);
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            await FailItem(item, MigrationConstants.ErrorCodes.FileNotFound,
                "Migration item has no source key.", ct);
            return;
        }

        var stat = await StatSourceAsync(connector, migration, item, sourceKey, ct);
        if (stat is null) return;

        // SHA256 dup check: prefer the pre-known hash (CSV manifest can supply
        // it); otherwise compute after download. Computing for every S3 item is
        // required to detect duplicates across a remote bucket.
        if (!string.IsNullOrWhiteSpace(item.Sha256)
            && await TryHandleKnownDuplicateAsync(item, item.Sha256, ct))
            return;

        var bytes = await DownloadSourceAsync(connector, migration, item, sourceKey, ct);
        if (bytes is null) return;

        await using (bytes)
        {
            var sha256 = await ResolveSha256Async(bytes, item.Sha256, ct);
            if (string.IsNullOrEmpty(item.Sha256)
                && await TryHandleKnownDuplicateAsync(item, sha256, ct))
                return;

            bytes.Position = 0;
            await CreateAssetFromMigrationAsync(migration, item, bytes, stat, sha256, sourceKey, ct);
        }
    }

    private async Task<MigrationObjectStat?> StatSourceAsync(
        IMigrationSourceConnector connector, Migration migration, MigrationItem item,
        string sourceKey, CancellationToken ct)
    {
        try
        {
            var stat = await connector.StatAsync(migration, sourceKey, ct);
            if (stat is not null) return stat;
            await FailItem(item, MigrationConstants.ErrorCodes.FileNotFound,
                $"Source object '{sourceKey}' not found (moved or deleted between scan/upload and ingest).", ct);
            return null;
        }
        catch (Exception ex)
        {
            await FailItem(item, MigrationConstants.ErrorCodes.FileStatFailed,
                $"Source stat failed for '{sourceKey}': {ex.Message}", ct);
            return null;
        }
    }

    private async Task<bool> TryHandleKnownDuplicateAsync(MigrationItem item, string sha256, CancellationToken ct)
    {
        var existing = await assetRepo.GetBySha256Async(sha256, ct);
        if (existing is null) return false;
        item.Sha256 = sha256;
        await MarkDuplicate(item, existing, sha256, ct);
        return true;
    }

    private async Task<Stream?> DownloadSourceAsync(
        IMigrationSourceConnector connector, Migration migration, MigrationItem item,
        string sourceKey, CancellationToken ct)
    {
        try
        {
            return await connector.DownloadAsync(migration, sourceKey, ct);
        }
        catch (Exception ex)
        {
            await FailItem(item, MigrationConstants.ErrorCodes.ProcessingError,
                $"Source download failed for '{sourceKey}': {ex.Message}", ct);
            return null;
        }
    }

    private static async Task<string> ResolveSha256Async(Stream bytes, string? known, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(known)) return known;
        bytes.Position = 0;
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(bytes, ct));
    }

    private async Task CreateAssetFromMigrationAsync(
        Migration migration, MigrationItem item, Stream bytes, MigrationObjectStat stat,
        string sha256, string sourceKey, CancellationToken ct)
    {
        var contentType = stat.ContentType;
        var assetType = DetermineAssetType(contentType);
        var assetId = Guid.NewGuid();
        var originalObjectKey = $"{Constants.StoragePrefixes.Originals}/{assetId}/{item.FileName}";

        var asset = new Asset
        {
            Id = assetId,
            AssetType = assetType,
            Status = AssetStatus.Processing,
            Title = item.Title ?? Path.GetFileNameWithoutExtension(item.FileName),
            Description = item.Description,
            Copyright = item.Copyright,
            Tags = item.Tags,
            MetadataJson = item.MetadataJson,
            ContentType = contentType,
            SizeBytes = stat.Size,
            Sha256 = sha256,
            OriginalObjectKey = originalObjectKey,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = migration.CreatedByUserId,
            UpdatedAt = DateTime.UtcNow
        };

        await minioAdapter.UploadAsync(_bucketName, originalObjectKey, bytes, contentType, ct);
        await assetRepo.CreateAsync(asset, ct);
        await AssignCollections(migration, item, asset, ct);
        await mediaProcessing.ScheduleProcessingAsync(
            assetId, assetType.ToDbString(), originalObjectKey, false, ct);

        item.Status = MigrationItemStatus.Succeeded;
        item.AssetId = assetId;
        item.Sha256 = sha256;
        item.ProcessedAt = DateTime.UtcNow;
        await migrationRepo.UpdateItemAsync(item, ct);

        logger.LogDebug(
            "Migration item {ItemId}: {SourceType} object {Key} → asset {AssetId} ({Size} bytes)",
            item.Id, migration.SourceType.ToDbString(), sourceKey, assetId, stat.Size);
    }

    private async Task MarkDuplicate(MigrationItem item, Asset existing, string sha256, CancellationToken ct)
    {
        item.Status = MigrationItemStatus.Skipped;
        item.ErrorCode = MigrationConstants.ErrorCodes.Duplicate;
        item.ErrorMessage = $"Asset with SHA256 {sha256} already exists (Asset ID: {existing.Id}).";
        item.AssetId = existing.Id;
        item.ProcessedAt = DateTime.UtcNow;
        await migrationRepo.UpdateItemAsync(item, ct);
    }

    private async Task FailItem(MigrationItem item, string errorCode, string errorMessage, CancellationToken ct)
    {
        item.Status = MigrationItemStatus.Failed;
        item.ErrorCode = errorCode;
        item.ErrorMessage = errorMessage.Length > MigrationConstants.Limits.MaxErrorMessageLength
            ? errorMessage[..MigrationConstants.Limits.MaxErrorMessageLength]
            : errorMessage;
        item.ProcessedAt = DateTime.UtcNow;
        await migrationRepo.UpdateItemAsync(item, ct);
    }

    private async Task AssignCollections(
        Migration migration, MigrationItem item, Asset asset, CancellationToken ct)
    {
        var collectionIds = new List<Guid>();

        // Add default collection if specified
        if (migration.DefaultCollectionId.HasValue)
            collectionIds.Add(migration.DefaultCollectionId.Value);

        // Resolve or create named collections
        foreach (var name in item.CollectionNames)
        {
            var existing = await collectionRepo.GetByNameAsync(name, ct);
            if (existing is not null)
            {
                if (!collectionIds.Contains(existing.Id))
                    collectionIds.Add(existing.Id);
            }
            else
            {
                // Create the collection
                var newCollection = new Collection
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = migration.CreatedByUserId
                };
                await collectionRepo.CreateAsync(newCollection, ct);
                await collectionAclRepo.SetAccessAsync(newCollection.Id, Constants.PrincipalTypes.User,
                    migration.CreatedByUserId, RoleHierarchy.Roles.Admin, ct);
                await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAccessTag(migration.CreatedByUserId), ct);
                collectionIds.Add(newCollection.Id);
            }
        }

        // Add asset to all resolved collections
        foreach (var collectionId in collectionIds)
        {
            await assetCollectionRepo.AddToCollectionAsync(
                asset.Id, collectionId, migration.CreatedByUserId, ct);
        }
    }

    private static AssetType DetermineAssetType(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return AssetType.Image;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return AssetType.Video;
        return AssetType.Document;
    }

    private async Task TryFinalizeMigration(Guid migrationId, CancellationToken ct)
    {
        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null || migration.Status is not MigrationStatus.Running)
            return;

        var connector = connectors.Resolve(migration.SourceType);
        var counts = await migrationRepo.GetItemCountsAsync(migrationId, ct);

        // Pending dispatchable items: staging-based sources only dispatch items
        // with a staged local file, so unstaged-pending items must not block
        // finalization. Remote-pull sources treat every pending item as
        // dispatchable, so Pending is the right gate.
        var pendingToDispatch = connector.RequiresLocalStaging
            ? counts.StagedPending
            : counts.Pending;

        if (pendingToDispatch > 0 || counts.Processing > 0)
            return;

        migration.ItemsSucceeded = counts.Succeeded;
        migration.ItemsFailed = counts.Failed;
        migration.ItemsSkipped = counts.Skipped;
        migration.FinishedAt = DateTime.UtcNow;

        migration.Status = ComputeTerminalStatus(connector, counts);

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
            "Migration {MigrationId} completed: {Succeeded} succeeded, {Failed} failed, {Skipped} skipped",
            migrationId, counts.Succeeded, counts.Failed, counts.Skipped);
    }

    /// <summary>
    /// Chooses the terminal status for a migration. Only staging-based sources
    /// can end in <see cref="MigrationStatus.PartiallyCompleted"/> (some items
    /// never got their file staged); remote-pull sources always complete once
    /// every pending item is terminal.
    /// </summary>
    internal static MigrationStatus ComputeTerminalStatus(
        IMigrationSourceConnector connector, MigrationItemCounts counts)
    {
        if (counts.Failed > 0)
            return MigrationStatus.CompletedWithErrors;

        if (!connector.RequiresLocalStaging)
            return MigrationStatus.Completed;

        return counts.Staged < counts.Total
            ? MigrationStatus.PartiallyCompleted
            : MigrationStatus.Completed;
    }
}
