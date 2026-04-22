using System.Security.Cryptography;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.Handlers;

/// <summary>
/// Scans a remote S3 bucket configured on an S3-source migration and materialises one
/// <see cref="MigrationItem"/> per object found. Returns the migration to Draft when done
/// so the admin can review and kick off the normal start flow; transitions to Failed on
/// credential / transport errors.
/// </summary>
public sealed class S3MigrationScanHandler(
    IMigrationRepository migrationRepo,
    IS3ConnectorClient s3Client,
    IMigrationSecretProtector secretProtector,
    IAuditService audit,
    ILogger<S3MigrationScanHandler> logger)
{
    public async Task HandleAsync(S3MigrationScanCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("S3 scan starting for migration {MigrationId}", command.MigrationId);

        var migration = await migrationRepo.GetByIdAsync(command.MigrationId, cancellationToken);
        if (migration is null)
        {
            logger.LogWarning("Migration {MigrationId} not found, skipping scan", command.MigrationId);
            return;
        }

        if (migration.Status is not MigrationStatus.Validating)
        {
            logger.LogWarning("Migration {MigrationId} is in {Status}, not Validating — skipping scan",
                command.MigrationId, migration.Status.ToDbString());
            return;
        }

        if (migration.SourceType is not MigrationSourceType.S3)
        {
            logger.LogWarning("Migration {MigrationId} is not an S3 migration — skipping scan",
                command.MigrationId);
            await RecordScanFailure(migration, "invalid_source_type",
                "Migration is not an S3 migration.", cancellationToken);
            return;
        }

        S3SourceConfigDto config;
        try
        {
            config = MigrationS3ConfigCodec.Read(migration.SourceConfig, secretProtector);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration {MigrationId}: S3 source config unreadable", migration.Id);
            await RecordScanFailure(migration, "invalid_config",
                "Stored S3 source configuration is invalid.", cancellationToken);
            return;
        }

        IReadOnlyList<S3ObjectInfo> objects;
        try
        {
            objects = await s3Client.ListObjectsAsync(config, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration {MigrationId}: S3 list-objects failed for bucket {Bucket}",
                migration.Id, config.Bucket);
            await RecordScanFailure(migration, "scan_failed", ex.Message, cancellationToken);
            return;
        }

        var items = new List<MigrationItem>(objects.Count);
        var rowNumber = 0;
        foreach (var obj in objects)
        {
            if (string.IsNullOrWhiteSpace(obj.Key) || obj.Key.EndsWith('/'))
            {
                // Directory placeholders ("foo/") and empty keys are not importable objects.
                continue;
            }
            rowNumber++;
            items.Add(BuildItem(migration, obj, rowNumber));
        }

        await migrationRepo.AddItemsAsync(items, cancellationToken);
        migration.ItemsTotal = items.Count;
        migration.Status = MigrationStatus.Draft;
        await migrationRepo.UpdateAsync(migration, cancellationToken);

        await audit.LogAsync(
            MigrationConstants.AuditEvents.S3ScanCompleted,
            Constants.ScopeTypes.Migration,
            migration.Id,
            actorUserId: null,
            new Dictionary<string, object>
            {
                ["bucket"] = config.Bucket,
                ["prefix"] = config.Prefix ?? string.Empty,
                ["objectsFound"] = items.Count
            },
            cancellationToken);

        logger.LogInformation("Migration {MigrationId}: S3 scan completed — {Count} items created",
            migration.Id, items.Count);
    }

    private static MigrationItem BuildItem(Migration migration, S3ObjectInfo obj, int rowNumber)
    {
        var rawName = Path.GetFileName(obj.Key);
        if (string.IsNullOrWhiteSpace(rawName))
            rawName = obj.Key;

        var filename = SanitizeFileName(rawName);
        var title = Path.GetFileNameWithoutExtension(filename);

        return new MigrationItem
        {
            Id = Guid.NewGuid(),
            MigrationId = migration.Id,
            Status = MigrationItemStatus.Pending,
            ExternalId = obj.Key,
            IdempotencyKey = ComputeIdempotencyKey(migration.Id, obj.Key),
            FileName = filename,
            SourcePath = obj.Key,
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            RowNumber = rowNumber,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string ComputeIdempotencyKey(Guid migrationId, string externalId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{migrationId}:{externalId}"));
        return Convert.ToHexStringLower(hash);
    }

    private static string SanitizeFileName(string fileName)
    {
        // Mirrors MigrationService.SanitizeFileName — the remote object key
        // segment after the last '/' is treated as the logical filename and must
        // pass the same path-traversal / invalid-char filter as uploaded files.
        var name = Path.GetFileName(fileName);
        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        name = name.Replace("..", "_").TrimStart('.', ' ').TrimEnd(' ');
        return string.IsNullOrWhiteSpace(name) ? "unnamed" : name;
    }

    private async Task RecordScanFailure(
        Migration migration, string errorCode, string errorMessage, CancellationToken ct)
    {
        migration.Status = MigrationStatus.Failed;
        migration.FinishedAt = DateTime.UtcNow;
        await migrationRepo.UpdateAsync(migration, ct);

        var truncated = errorMessage.Length > MigrationConstants.Limits.MaxErrorMessageLength
            ? errorMessage[..MigrationConstants.Limits.MaxErrorMessageLength]
            : errorMessage;

        await audit.LogAsync(
            MigrationConstants.AuditEvents.S3ScanFailed,
            Constants.ScopeTypes.Migration,
            migration.Id,
            actorUserId: null,
            new Dictionary<string, object>
            {
                ["errorCode"] = errorCode,
                ["errorMessage"] = truncated
            },
            ct);
    }
}
