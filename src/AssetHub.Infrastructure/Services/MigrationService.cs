using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
using Wolverine;

namespace AssetHub.Infrastructure.Services;

public sealed class MigrationService(
    IMigrationRepository migrationRepo,
    ICollectionRepository collectionRepo,
    ICollectionAclRepository collectionAclRepo,
    IMinIOAdapter minioAdapter,
    IOptions<MinIOSettings> minioSettings,
    IAuditService audit,
    IMessageBus messageBus,
    IMigrationSecretProtector secretProtector,
    IMigrationSourceConnectorRegistry connectors,
    HybridCache cache,
    CurrentUser currentUser,
    ILogger<MigrationService> logger) : IMigrationService
{
    private readonly string _bucketName = minioSettings.Value.BucketName;
    public async Task<ServiceResult<MigrationResponseDto>> CreateAsync(CreateMigrationDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can create migrations.");

        var sourceType = dto.SourceType.ToMigrationSourceType();
        var connector = connectors.Resolve(sourceType);

        // Delegate source-specific config validation + encoding to the connector.
        // Each connector also rejects config that belongs to a different source.
        var encodedConfigResult = connector.EncodeConfig(dto);
        if (!encodedConfigResult.IsSuccess)
            return encodedConfigResult.Error!;

        // Validate mutually exclusive collection fields
        if (dto.DefaultCollectionId.HasValue && !string.IsNullOrWhiteSpace(dto.DefaultCollectionName))
            return ServiceError.BadRequest("Specify either an existing collection or a new collection name, not both.");

        Guid? resolvedCollectionId = dto.DefaultCollectionId;

        if (dto.DefaultCollectionId.HasValue)
        {
            var collectionExists = await collectionRepo.ExistsAsync(dto.DefaultCollectionId.Value, ct);
            if (!collectionExists)
                return ServiceError.BadRequest("Default collection not found.");
        }
        else if (!string.IsNullOrWhiteSpace(dto.DefaultCollectionName))
        {
            var trimmedName = dto.DefaultCollectionName.Trim();

            // Check if a collection with this name already exists
            var existing = await collectionRepo.GetByNameAsync(trimmedName, ct);
            if (existing is not null)
            {
                resolvedCollectionId = existing.Id;
            }
            else
            {
                var newCollection = new Collection
                {
                    Id = Guid.NewGuid(),
                    Name = trimmedName,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = currentUser.UserId
                };
                await collectionRepo.CreateAsync(newCollection, ct);
                await collectionAclRepo.SetAccessAsync(newCollection.Id, Constants.PrincipalTypes.User,
                    currentUser.UserId, RoleHierarchy.Roles.Admin, ct);
                await cache.RemoveByTagAsync(CacheKeys.Tags.CollectionAccessTag(currentUser.UserId), ct);
                resolvedCollectionId = newCollection.Id;

                logger.LogInformation("Created new collection {CollectionId} '{CollectionName}' for migration",
                    newCollection.Id, trimmedName);
            }
        }

        var migration = new Migration
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            SourceType = sourceType,
            Status = MigrationStatus.Draft,
            DefaultCollectionId = resolvedCollectionId,
            DryRun = dto.DryRun,
            CreatedByUserId = currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };

        if (encodedConfigResult.Value is { } encodedConfig)
            migration.SourceConfig = encodedConfig;

        await migrationRepo.CreateAsync(migration, ct);

        await audit.LogAsync(MigrationConstants.AuditEvents.Created, Constants.ScopeTypes.Migration, migration.Id,
            currentUser.UserId, new Dictionary<string, object>
            {
                ["name"] = migration.Name,
                ["sourceType"] = dto.SourceType,
                ["dryRun"] = dto.DryRun
            }, ct);

        logger.LogInformation("Migration {MigrationId} created by {UserId}", migration.Id, currentUser.UserId);

        return ToDto(migration);
    }

    public async Task<ServiceResult<MigrationResponseDto>> UploadManifestAsync(
        Guid migrationId, Stream csvStream, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can upload migration manifests.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.Status != MigrationStatus.Draft)
            return ServiceError.BadRequest("Manifest can only be uploaded to a draft migration.");

        // Remove any existing items (re-upload replaces)
        await migrationRepo.RemoveAllItemsAsync(migrationId, ct);

        var items = await ParseCsvManifestAsync(migration, csvStream, ct);
        if (items.Count == 0)
            return ServiceError.BadRequest("CSV manifest contains no valid rows.");

        await migrationRepo.AddItemsAsync(items, ct);

        migration.ItemsTotal = items.Count;
        await migrationRepo.UpdateAsync(migration, ct);

        logger.LogInformation("Migration {MigrationId}: parsed {ItemCount} items from CSV manifest",
            migrationId, items.Count);

        return ToDto(migration);
    }

    public async Task<ServiceResult> StartAsync(Guid migrationId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can start migrations.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.Status is not (MigrationStatus.Draft or MigrationStatus.PartiallyCompleted))
            return ServiceError.BadRequest($"Cannot start a migration in '{migration.Status.ToDbString()}' status.");

        if (migration.ItemsTotal == 0)
            return ServiceError.BadRequest("Upload a manifest before starting the migration.");

        migration.Status = MigrationStatus.Running;
        migration.StartedAt = DateTime.UtcNow;
        await migrationRepo.UpdateAsync(migration, ct);

        await messageBus.PublishAsync(new StartMigrationCommand { MigrationId = migrationId });

        await audit.LogAsync(MigrationConstants.AuditEvents.Started, Constants.ScopeTypes.Migration, migrationId,
            currentUser.UserId, ct: ct);

        logger.LogInformation("Migration {MigrationId} started with {ItemCount} items",
            migrationId, migration.ItemsTotal);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult> StartS3ScanAsync(Guid migrationId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can scan S3 migrations.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.SourceType is not MigrationSourceType.S3)
            return ServiceError.BadRequest("Scan is only valid for S3-source migrations.");

        if (migration.Status is not MigrationStatus.Draft)
            return ServiceError.BadRequest($"Cannot scan a migration in '{migration.Status.ToDbString()}' status.");

        // Decode the stored config to capture bucket+prefix for the audit event and to
        // fail fast if the stored ciphertext is corrupted, before the worker starts.
        S3SourceConfigDto config;
        try
        {
            config = MigrationS3ConfigCodec.Read(migration.SourceConfig, secretProtector);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration {MigrationId}: S3 source config is invalid or unreadable", migrationId);
            return ServiceError.BadRequest("Stored S3 source configuration is invalid. Recreate the migration.");
        }

        migration.Status = MigrationStatus.Validating;
        await migrationRepo.UpdateAsync(migration, ct);

        await messageBus.PublishAsync(new S3MigrationScanCommand { MigrationId = migrationId });

        await audit.LogAsync(
            MigrationConstants.AuditEvents.S3ScanStarted,
            Constants.ScopeTypes.Migration,
            migrationId,
            currentUser.UserId,
            new Dictionary<string, object>
            {
                ["bucket"] = config.Bucket,
                ["prefix"] = config.Prefix ?? string.Empty
            },
            ct);

        logger.LogInformation("Migration {MigrationId}: S3 scan queued for bucket {Bucket}", migrationId, config.Bucket);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult> CancelAsync(Guid migrationId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can cancel migrations.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.Status is not (MigrationStatus.Running or MigrationStatus.Validating))
            return ServiceError.BadRequest($"Cannot cancel a migration in '{migration.Status.ToDbString()}' status.");

        migration.Status = MigrationStatus.Cancelled;
        migration.FinishedAt = DateTime.UtcNow;

        var counts = await migrationRepo.GetItemCountsAsync(migrationId, ct);
        migration.ItemsSucceeded = counts.Succeeded;
        migration.ItemsFailed = counts.Failed;
        migration.ItemsSkipped = counts.Skipped;

        await migrationRepo.UpdateAsync(migration, ct);

        await audit.LogAsync(MigrationConstants.AuditEvents.Cancelled, Constants.ScopeTypes.Migration, migrationId,
            currentUser.UserId, ct: ct);

        logger.LogInformation("Migration {MigrationId} cancelled", migrationId);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<MigrationResponseDto>> GetByIdAsync(Guid migrationId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can view migrations.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        // Refresh counts from items for running migrations
        if (migration.Status is MigrationStatus.Running)
        {
            var counts = await migrationRepo.GetItemCountsAsync(migrationId, ct);
            migration.ItemsSucceeded = counts.Succeeded;
            migration.ItemsFailed = counts.Failed;
            migration.ItemsSkipped = counts.Skipped;
        }

        var itemCounts = await migrationRepo.GetItemCountsAsync(migrationId, ct);
        return ToDto(migration, itemCounts.Staged);
    }

    public async Task<ServiceResult<MigrationListResponse>> ListAsync(int skip, int take, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can view migrations.");

        var migrations = await migrationRepo.ListAsync(skip, take, ct);
        var totalCount = await migrationRepo.CountAsync(ct);

        var dtos = new List<MigrationResponseDto>();
        foreach (var m in migrations)
        {
            var counts = await migrationRepo.GetItemCountsAsync(m.Id, ct);
            dtos.Add(ToDto(m, counts.Staged));
        }

        return new MigrationListResponse
        {
            Migrations = dtos,
            TotalCount = totalCount
        };
    }

    public async Task<ServiceResult<MigrationProgressDto>> GetProgressAsync(Guid migrationId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can view migration progress.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        var counts = await migrationRepo.GetItemCountsAsync(migrationId, ct);

        return new MigrationProgressDto
        {
            Id = migration.Id,
            Status = migration.Status.ToDbString(),
            ItemsTotal = counts.Total,
            ItemsStaged = counts.Staged,
            ItemsSucceeded = counts.Succeeded,
            ItemsFailed = counts.Failed,
            ItemsSkipped = counts.Skipped
        };
    }

    public async Task<ServiceResult<MigrationItemListResponse>> GetItemsAsync(
        Guid migrationId, string? statusFilter, int skip, int take, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can view migration items.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        var items = await migrationRepo.GetItemsAsync(migrationId, statusFilter, skip, take, ct);
        var totalCount = await migrationRepo.CountItemsAsync(migrationId, statusFilter, ct);

        return new MigrationItemListResponse
        {
            Items = items.Select(ToItemDto).ToList(),
            TotalCount = totalCount
        };
    }

    public async Task<ServiceResult> DeleteAsync(Guid migrationId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can delete migrations.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.Status is MigrationStatus.Running or MigrationStatus.Validating)
            return ServiceError.BadRequest("Cannot delete a running migration. Cancel it first.");

        await migrationRepo.DeleteAsync(migrationId, ct);

        await audit.LogAsync(MigrationConstants.AuditEvents.Deleted, Constants.ScopeTypes.Migration, migrationId,
            currentUser.UserId, ct: ct);

        logger.LogInformation("Migration {MigrationId} deleted", migrationId);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult> RetryFailedAsync(Guid migrationId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can retry migrations.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.Status is not (MigrationStatus.CompletedWithErrors or MigrationStatus.Failed))
            return ServiceError.BadRequest("Can only retry migrations that have failed items.");

        var failedItems = await migrationRepo.GetFailedItemsAsync(migrationId, ct);
        if (failedItems.Count == 0)
            return ServiceError.BadRequest("No failed items to retry.");

        // Reset failed items to pending
        foreach (var item in failedItems)
        {
            item.Status = MigrationItemStatus.Pending;
            item.ErrorCode = null;
            item.ErrorMessage = null;
            await migrationRepo.UpdateItemAsync(item, ct);
        }

        migration.Status = MigrationStatus.Running;
        migration.StartedAt = DateTime.UtcNow;
        migration.FinishedAt = null;
        await migrationRepo.UpdateAsync(migration, ct);

        await messageBus.PublishAsync(new StartMigrationCommand { MigrationId = migrationId });

        await audit.LogAsync(MigrationConstants.AuditEvents.Retried, Constants.ScopeTypes.Migration, migrationId,
            currentUser.UserId, new Dictionary<string, object>
            {
                ["failedCount"] = failedItems.Count
            }, ct);

        logger.LogInformation("Migration {MigrationId}: retrying {FailedCount} failed items",
            migrationId, failedItems.Count);

        return ServiceResult.Success;
    }

    // ── CSV Parsing ─────────────────────────────────────────────────────

    private async Task<List<MigrationItem>> ParseCsvManifestAsync(
        Migration migration, Stream csvStream, CancellationToken ct)
    {
        var items = new List<MigrationItem>();
        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine))
            return items;

        var headers = ParseCsvLine(headerLine);
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
            headerMap[headers[i].Trim()] = i;

        var rowNumber = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            var item = MapCsvRowToItem(migration, headerMap, fields, rowNumber);
            if (item is not null)
                items.Add(item);
        }

        return items;
    }

    private MigrationItem? MapCsvRowToItem(
        Migration migration,
        Dictionary<string, int> headerMap,
        string[] fields,
        int rowNumber)
    {
        string GetField(string name)
        {
            return headerMap.TryGetValue(name, out var idx) && idx < fields.Length
                ? fields[idx].Trim()
                : string.Empty;
        }

        var filename = GetField(MigrationConstants.CsvHeaders.Filename);
        if (string.IsNullOrWhiteSpace(filename))
        {
            logger.LogWarning("Migration {MigrationId}: skipping row {Row} — missing filename",
                migration.Id, rowNumber);
            return null;
        }

        // Sanitize filename to match what will be used during file upload
        filename = SanitizeFileName(filename);

        var externalId = GetField(MigrationConstants.CsvHeaders.ExternalId);
        if (string.IsNullOrWhiteSpace(externalId))
            externalId = filename; // Use filename as fallback external ID

        var idempotencyKey = ComputeIdempotencyKey(migration.Id, externalId);

        var title = GetField(MigrationConstants.CsvHeaders.Title);
        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(filename);

        var tags = new List<string>();
        var rawTags = GetField(MigrationConstants.CsvHeaders.Tags);
        if (!string.IsNullOrWhiteSpace(rawTags))
            tags = rawTags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var collectionNames = new List<string>();
        var rawCollections = GetField(MigrationConstants.CsvHeaders.CollectionNames);
        if (!string.IsNullOrWhiteSpace(rawCollections))
            collectionNames = rawCollections.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var metadata = new Dictionary<string, object>();
        foreach (var (header, idx) in headerMap)
        {
            if (header.StartsWith(MigrationConstants.CsvHeaders.MetadataPrefix, StringComparison.OrdinalIgnoreCase) && idx < fields.Length)
            {
                var key = header[MigrationConstants.CsvHeaders.MetadataPrefix.Length..];
                var val = fields[idx].Trim();
                if (!string.IsNullOrEmpty(val))
                    metadata[key] = val;
            }
        }

        var description = GetField(MigrationConstants.CsvHeaders.Description);
        var copyright = GetField(MigrationConstants.CsvHeaders.Copyright);
        var sha256 = GetField(MigrationConstants.CsvHeaders.Sha256);

        return new MigrationItem
        {
            Id = Guid.NewGuid(),
            MigrationId = migration.Id,
            Status = MigrationItemStatus.Pending,
            ExternalId = externalId,
            IdempotencyKey = idempotencyKey,
            FileName = filename,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            Copyright = string.IsNullOrWhiteSpace(copyright) ? null : copyright,
            Tags = tags,
            CollectionNames = collectionNames,
            MetadataJson = metadata,
            Sha256 = string.IsNullOrWhiteSpace(sha256) ? null : sha256,
            RowNumber = rowNumber,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Parse a CSV line with proper quote handling.
    /// </summary>
    internal static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static string ComputeIdempotencyKey(Guid migrationId, string externalId)
    {
        var input = $"{migrationId}:{externalId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    // ── Mapping helpers ─────────────────────────────────────────────────

    private static MigrationResponseDto ToDto(Migration m, int? itemsStaged = null) => new()
    {
        Id = m.Id,
        Name = m.Name,
        SourceType = m.SourceType.ToDbString(),
        Status = m.Status.ToDbString(),
        DefaultCollectionId = m.DefaultCollectionId,
        DryRun = m.DryRun,
        ItemsTotal = m.ItemsTotal,
        ItemsStaged = itemsStaged ?? 0,
        ItemsSucceeded = m.ItemsSucceeded,
        ItemsFailed = m.ItemsFailed,
        ItemsSkipped = m.ItemsSkipped,
        CreatedByUserId = m.CreatedByUserId,
        CreatedAt = m.CreatedAt,
        StartedAt = m.StartedAt,
        FinishedAt = m.FinishedAt
    };

    private static MigrationItemResponseDto ToItemDto(MigrationItem i) => new()
    {
        Id = i.Id,
        MigrationId = i.MigrationId,
        Status = i.Status.ToDbString(),
        ExternalId = i.ExternalId,
        FileName = i.FileName,
        SourcePath = i.SourcePath,
        Title = i.Title,
        Description = i.Description,
        Copyright = i.Copyright,
        Tags = i.Tags,
        CollectionNames = i.CollectionNames,
        MetadataJson = i.MetadataJson,
        Sha256 = i.Sha256,
        ErrorCode = i.ErrorCode,
        ErrorMessage = i.ErrorMessage,
        AttemptCount = i.AttemptCount,
        IsFileStaged = i.IsFileStaged,
        AssetId = i.AssetId,
        RowNumber = i.RowNumber,
        CreatedAt = i.CreatedAt,
        ProcessedAt = i.ProcessedAt
    };

    // ── Staging file uploads ────────────────────────────────────────────

    public async Task<ServiceResult> UploadStagingFileAsync(
        Guid migrationId, string fileName, Stream fileStream, string contentType, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can upload migration files.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.Status is not MigrationStatus.Draft and not MigrationStatus.Validating
            and not MigrationStatus.PartiallyCompleted and not MigrationStatus.PartiallyCompleted and not MigrationStatus.CompletedWithErrors and not MigrationStatus.Failed)
            return ServiceError.BadRequest("Files can only be uploaded to a draft, validating, or incomplete migration.");

        var safeFileName = SanitizeFileName(fileName);
        var stagingKey = MigrationConstants.StagingKey(migrationId, safeFileName);

        await minioAdapter.UploadAsync(_bucketName, stagingKey, fileStream, contentType, ct);
        await migrationRepo.MarkItemsStagedAsync(migrationId, [safeFileName], ct);

        logger.LogDebug("Uploaded staging file {FileName} for migration {MigrationId}", safeFileName, migrationId);
        return ServiceResult.Success;
    }

    public async Task<ServiceResult<int>> UploadStagingFilesAsync(
        Guid migrationId, IEnumerable<(string FileName, Stream Stream, string ContentType)> files, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can upload migration files.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.Status is not MigrationStatus.Draft and not MigrationStatus.Validating
            and not MigrationStatus.PartiallyCompleted and not MigrationStatus.CompletedWithErrors and not MigrationStatus.Failed)
            return ServiceError.BadRequest("Files can only be uploaded to a draft, validating, or incomplete migration.");

        var count = 0;
        var stagedFileNames = new List<string>();
        foreach (var (fName, stream, contentType) in files)
        {
            var safeFileName = SanitizeFileName(fName);
            var stagingKey = MigrationConstants.StagingKey(migrationId, safeFileName);
            await minioAdapter.UploadAsync(_bucketName, stagingKey, stream, contentType, ct);
            stagedFileNames.Add(safeFileName);
            count++;
        }

        if (stagedFileNames.Count > 0)
        {
            await migrationRepo.MarkItemsStagedAsync(migrationId, stagedFileNames, ct);
        }

        logger.LogInformation("Uploaded {Count} staging files for migration {MigrationId}", count, migrationId);
        return count;
    }

    public async Task<ServiceResult> UnstageMigrationItemAsync(Guid migrationId, Guid itemId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can unstage migration files.");

        var migration = await migrationRepo.GetByIdAsync(migrationId, ct);
        if (migration is null)
            return ServiceError.NotFound("Migration not found.");

        if (migration.Status is not (MigrationStatus.Draft or MigrationStatus.PartiallyCompleted
            or MigrationStatus.CompletedWithErrors or MigrationStatus.Failed))
            return ServiceError.BadRequest("Files can only be unstaged from a draft or incomplete migration.");

        var item = await migrationRepo.GetItemByIdAsync(itemId, ct);
        if (item is null || item.MigrationId != migrationId)
            return ServiceError.NotFound("Migration item not found.");

        if (!item.IsFileStaged)
            return ServiceError.BadRequest("This item does not have a staged file.");

        // Remove the file from MinIO staging
        var stagingKey = MigrationConstants.StagingKey(migrationId, item.FileName);
        try
        {
            await minioAdapter.DeleteAsync(_bucketName, stagingKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete staging file {Key} — continuing with unstage", stagingKey);
        }

        // Unmark the item
        item.IsFileStaged = false;
        await migrationRepo.UpdateItemAsync(item, ct);

        logger.LogDebug("Unstaged file {FileName} for migration {MigrationId} item {ItemId}",
            item.FileName, migrationId, itemId);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<int>> BulkDeleteAsync(string filter, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can delete migrations.");

        var statuses = filter.ToLowerInvariant() switch
        {
            "completed" => new List<MigrationStatus>
            {
                MigrationStatus.Completed,
                MigrationStatus.PartiallyCompleted,
                MigrationStatus.CompletedWithErrors,
                MigrationStatus.Failed,
                MigrationStatus.Cancelled
            },
            "draft" => new List<MigrationStatus> { MigrationStatus.Draft },
            "all" => new List<MigrationStatus>
            {
                MigrationStatus.Draft,
                MigrationStatus.Completed,
                MigrationStatus.PartiallyCompleted,
                MigrationStatus.CompletedWithErrors,
                MigrationStatus.Failed,
                MigrationStatus.Cancelled
            },
            _ => (List<MigrationStatus>?)null
        };

        if (statuses is null)
            return ServiceError.BadRequest($"Invalid filter '{filter}'. Use 'completed', 'draft', or 'all'.");

        var deleted = await migrationRepo.DeleteByStatusAsync(statuses, ct);

        await audit.LogAsync(MigrationConstants.AuditEvents.BulkDeleted, Constants.ScopeTypes.Migration, null,
            currentUser.UserId, new Dictionary<string, object>
            {
                ["filter"] = filter,
                ["deletedCount"] = deleted
            }, ct);

        logger.LogInformation("Bulk deleted {Count} migrations with filter '{Filter}'", deleted, filter);

        return deleted;
    }

    private static string SanitizeFileName(string fileName)
    {
        // Get just the filename without path components (prevents path traversal)
        var name = Path.GetFileName(fileName);
        // Replace invalid characters and directory separators
        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        // Strip leading dots/spaces to prevent hidden files
        name = name.Replace("..", "_").TrimStart('.', ' ').TrimEnd(' ');
        return string.IsNullOrWhiteSpace(name) ? "unnamed" : name;
    }
}
