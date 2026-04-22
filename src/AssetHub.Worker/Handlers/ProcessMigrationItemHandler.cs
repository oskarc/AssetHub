using System.Security.Cryptography;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.Handlers;

public sealed class ProcessMigrationItemHandler(
    IMigrationRepository migrationRepo,
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    ICollectionRepository collectionRepo,
    ICollectionAclRepository collectionAclRepo,
    IMinIOAdapter minioAdapter,
    IS3ConnectorClient s3Client,
    IMigrationSecretProtector secretProtector,
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
            else if (migration.SourceType is MigrationSourceType.S3)
            {
                await ProcessS3ItemAsync(migration, item, cancellationToken);
            }
            else
            {
                await ProcessItemAsync(migration, item, cancellationToken);
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

    private async Task ProcessItemAsync(Migration migration, MigrationItem item, CancellationToken ct)
    {
        // 1. Check staging bucket for the file
        var stagingKey = MigrationConstants.StagingKey(migration.Id, item.FileName);
        var exists = await minioAdapter.ExistsAsync(_bucketName, stagingKey, ct);
        if (!exists)
        {
            item.Status = MigrationItemStatus.Failed;
            item.ErrorCode = MigrationConstants.ErrorCodes.FileNotFound;
            item.ErrorMessage = $"File '{item.FileName}' not found in staging area.";
            item.ProcessedAt = DateTime.UtcNow;
            await migrationRepo.UpdateItemAsync(item, ct);
            return;
        }

        // 2. Check for duplicate by SHA256
        if (!string.IsNullOrWhiteSpace(item.Sha256))
        {
            var existing = await assetRepo.GetBySha256Async(item.Sha256, ct);
            if (existing is not null)
            {
                item.Status = MigrationItemStatus.Skipped;
                item.ErrorCode = MigrationConstants.ErrorCodes.Duplicate;
                item.ErrorMessage = $"Asset with SHA256 {item.Sha256} already exists (Asset ID: {existing.Id}).";
                item.AssetId = existing.Id;
                item.ProcessedAt = DateTime.UtcNow;
                await migrationRepo.UpdateItemAsync(item, ct);
                return;
            }
        }

        // 3. Get file info from staging
        var stat = await minioAdapter.StatObjectAsync(_bucketName, stagingKey, ct);
        if (stat is null)
        {
            item.Status = MigrationItemStatus.Failed;
            item.ErrorCode = MigrationConstants.ErrorCodes.FileStatFailed;
            item.ErrorMessage = "Could not read file metadata from staging area.";
            item.ProcessedAt = DateTime.UtcNow;
            await migrationRepo.UpdateItemAsync(item, ct);
            return;
        }

        // 4. Determine asset type from content type
        var contentType = stat.ContentType;
        var assetType = DetermineAssetType(contentType);

        // 5. Create asset entity
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
            Sha256 = item.Sha256,
            OriginalObjectKey = originalObjectKey,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = migration.CreatedByUserId,
            UpdatedAt = DateTime.UtcNow
        };

        // 6. Copy from staging to final location
        using var stream = await minioAdapter.DownloadAsync(_bucketName, stagingKey, ct);
        await minioAdapter.UploadAsync(_bucketName, originalObjectKey, stream, contentType, ct);

        // 7. Persist asset
        await assetRepo.CreateAsync(asset, ct);

        // 8. Assign to collections
        await AssignCollections(migration, item, asset, ct);

        // 9. Schedule media processing
        await mediaProcessing.ScheduleProcessingAsync(
            assetId, assetType.ToDbString(), originalObjectKey, false, ct);

        // 10. Update migration item
        item.Status = MigrationItemStatus.Succeeded;
        item.AssetId = assetId;
        item.ProcessedAt = DateTime.UtcNow;
        await migrationRepo.UpdateItemAsync(item, ct);

        logger.LogDebug("Migration item {ItemId}: asset {AssetId} created from {FileName}",
            item.Id, assetId, item.FileName);
    }

    private async Task ProcessS3ItemAsync(Migration migration, MigrationItem item, CancellationToken ct)
    {
        // 1. Decode the stored per-migration S3 config.
        S3SourceConfigDto config;
        try
        {
            config = MigrationS3ConfigCodec.Read(migration.SourceConfig, secretProtector);
        }
        catch (Exception ex)
        {
            await FailItem(item, MigrationConstants.ErrorCodes.ProcessingError,
                $"S3 source config is invalid or unreadable: {ex.Message}", ct);
            return;
        }

        var sourceKey = !string.IsNullOrWhiteSpace(item.SourcePath)
            ? item.SourcePath
            : item.ExternalId;
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            await FailItem(item, MigrationConstants.ErrorCodes.FileNotFound,
                "Migration item has no S3 source key.", ct);
            return;
        }

        // 2. Stat the remote object to get size + content-type up-front.
        ObjectStatInfo? stat;
        try
        {
            stat = await s3Client.StatObjectAsync(config, sourceKey, ct);
        }
        catch (Exception ex)
        {
            await FailItem(item, MigrationConstants.ErrorCodes.FileStatFailed,
                $"S3 stat failed for '{sourceKey}': {ex.Message}", ct);
            return;
        }

        if (stat is null)
        {
            await FailItem(item, MigrationConstants.ErrorCodes.FileNotFound,
                $"S3 object '{sourceKey}' not found (moved or deleted between scan and ingest).", ct);
            return;
        }

        // 3. Download bytes into memory and hash them for duplicate detection.
        Stream? bytes = null;
        try
        {
            try
            {
                bytes = await s3Client.DownloadObjectAsync(config, sourceKey, ct);
            }
            catch (Exception ex)
            {
                await FailItem(item, MigrationConstants.ErrorCodes.ProcessingError,
                    $"S3 download failed for '{sourceKey}': {ex.Message}", ct);
                return;
            }

            bytes.Position = 0;
            var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(bytes, ct));
            bytes.Position = 0;

            // 4. Cross-library duplicate detection. Mark the existing asset as the
            // target so admins can still click through from the migration report.
            var existing = await assetRepo.GetBySha256Async(sha256, ct);
            if (existing is not null)
            {
                item.Status = MigrationItemStatus.Skipped;
                item.ErrorCode = MigrationConstants.ErrorCodes.Duplicate;
                item.ErrorMessage = $"Asset with SHA256 {sha256} already exists (Asset ID: {existing.Id}).";
                item.AssetId = existing.Id;
                item.Sha256 = sha256;
                item.ProcessedAt = DateTime.UtcNow;
                await migrationRepo.UpdateItemAsync(item, ct);
                return;
            }

            // 5. Upload bytes to the final originals location and persist the asset.
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
                "Migration item {ItemId}: S3 object {Key} → asset {AssetId} ({Size} bytes)",
                item.Id, sourceKey, assetId, stat.Size);
        }
        finally
        {
            if (bytes is not null)
                await bytes.DisposeAsync();
        }
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

        var counts = await migrationRepo.GetItemCountsAsync(migrationId, ct);

        // Pending dispatchable items: S3 treats every pending item as dispatchable
        // (bytes live remotely); CSV only dispatches items with a staged local file,
        // so unstaged-pending items should not block finalization.
        var pendingToDispatch = migration.SourceType is MigrationSourceType.S3
            ? counts.Pending
            : counts.StagedPending;

        if (pendingToDispatch > 0 || counts.Processing > 0)
            return;

        migration.ItemsSucceeded = counts.Succeeded;
        migration.ItemsFailed = counts.Failed;
        migration.ItemsSkipped = counts.Skipped;
        migration.FinishedAt = DateTime.UtcNow;

        migration.Status = ComputeTerminalStatus(migration, counts);

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
    /// Chooses the terminal status for a migration. S3 migrations never stage files
    /// locally, so <c>counts.Staged &lt; counts.Total</c> is always true for them
    /// and must not be interpreted as "partially completed".
    /// </summary>
    internal static MigrationStatus ComputeTerminalStatus(Migration migration, MigrationItemCounts counts)
    {
        if (counts.Failed > 0)
            return MigrationStatus.CompletedWithErrors;

        // Staging concept only applies to local-file sources (CSV upload).
        if (migration.SourceType is MigrationSourceType.S3)
            return MigrationStatus.Completed;

        return counts.Staged < counts.Total
            ? MigrationStatus.PartiallyCompleted
            : MigrationStatus.Completed;
    }
}
