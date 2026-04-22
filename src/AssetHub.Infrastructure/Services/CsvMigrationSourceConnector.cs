using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Connector for the CSV-manifest / staged-file import flow. Items are created
/// from a CSV manifest and their bytes live in the internal MinIO staging bucket
/// at <see cref="MigrationConstants.StagingKey"/>. Nothing to scan — the manifest
/// upload endpoint is the seeding mechanism.
/// </summary>
public sealed class CsvMigrationSourceConnector(
    IMinIOAdapter minioAdapter,
    IOptions<MinIOSettings> minioSettings) : IMigrationSourceConnector
{
    private readonly string _bucketName = minioSettings.Value.BucketName;

    public MigrationSourceType SourceType => MigrationSourceType.CsvUpload;
    public bool RequiresLocalStaging => true;
    public bool SupportsScan => false;

    public ServiceResult<Dictionary<string, object>?> EncodeConfig(CreateMigrationDto dto)
    {
        if (dto.S3Config is not null)
            return ServiceError.BadRequest("S3 source config must not be provided for non-S3 migrations.");
        // CSV has no persisted source config.
        return (Dictionary<string, object>?)null;
    }

    public Task<IReadOnlyList<MigrationObjectInfo>> ScanAsync(Migration migration, CancellationToken ct)
        => throw new NotSupportedException("CSV-source migrations are seeded from a manifest upload, not a scan.");

    public string ResolveSourceKey(Migration migration, MigrationItem item)
        => MigrationConstants.StagingKey(migration.Id, item.FileName);

    public async Task<MigrationObjectStat?> StatAsync(Migration migration, string sourceKey, CancellationToken ct)
    {
        // The staged-file path explicitly checks existence first because StatObject
        // throws for missing keys on some MinIO builds; keep that probe here.
        if (!await minioAdapter.ExistsAsync(_bucketName, sourceKey, ct))
            return null;

        var stat = await minioAdapter.StatObjectAsync(_bucketName, sourceKey, ct);
        return stat is null
            ? null
            : new MigrationObjectStat(stat.Size, stat.ContentType, stat.ETag);
    }

    public Task<Stream> DownloadAsync(Migration migration, string sourceKey, CancellationToken ct)
        => minioAdapter.DownloadAsync(_bucketName, sourceKey, ct);
}
