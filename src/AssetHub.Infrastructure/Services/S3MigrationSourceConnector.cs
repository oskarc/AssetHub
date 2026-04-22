using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.Exceptions;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Connector for S3-compatible pull sources (AWS S3, MinIO, Wasabi, R2, etc.).
/// Builds an on-demand MinIO SDK client from the per-migration credentials
/// stored in <c>Migration.SourceConfig</c>, encoded/decoded via
/// <see cref="MigrationS3ConfigCodec"/>.
/// </summary>
public sealed class S3MigrationSourceConnector(
    IMigrationSecretProtector secretProtector,
    ILogger<S3MigrationSourceConnector> logger) : IMigrationSourceConnector
{
    public MigrationSourceType SourceType => MigrationSourceType.S3;
    public bool RequiresLocalStaging => false;
    public bool SupportsScan => true;

    public ServiceResult<Dictionary<string, object>?> EncodeConfig(CreateMigrationDto dto)
    {
        if (dto.S3Config is null)
            return ServiceError.BadRequest("S3 source config is required when sourceType is 's3'.");
        return (Dictionary<string, object>?)MigrationS3ConfigCodec.Write(dto.S3Config, secretProtector);
    }

    public async Task<IReadOnlyList<MigrationObjectInfo>> ScanAsync(Migration migration, CancellationToken ct)
    {
        var config = MigrationS3ConfigCodec.Read(migration.SourceConfig, secretProtector);
        using var client = BuildClient(config);

        var listArgs = new ListObjectsArgs()
            .WithBucket(config.Bucket)
            .WithRecursive(true);
        if (!string.IsNullOrWhiteSpace(config.Prefix))
            listArgs = listArgs.WithPrefix(config.Prefix);

        var items = new List<MigrationObjectInfo>();
        var completion = new TaskCompletionSource();
        using var cancelReg = ct.Register(() => completion.TrySetCanceled(ct));

        var subscription = client.ListObjectsAsync(listArgs, ct).Subscribe(
            onNext: item =>
            {
                // MinIO's Item uses ulong for Size; migration items expect long. Guard
                // against the unlikely > 8 EiB object by clamping rather than throwing.
                var size = item.Size > long.MaxValue ? long.MaxValue : (long)item.Size;
                items.Add(new MigrationObjectInfo(item.Key, size, item.ETag ?? string.Empty));
            },
            onError: ex =>
            {
                logger.LogWarning(ex, "S3 list-objects failed for bucket {Bucket}", config.Bucket);
                completion.TrySetException(ex);
            },
            onCompleted: () => completion.TrySetResult());

        try
        {
            await completion.Task;
        }
        finally
        {
            subscription.Dispose();
        }

        return items;
    }

    public string ResolveSourceKey(Migration migration, MigrationItem item)
        => !string.IsNullOrWhiteSpace(item.SourcePath)
            ? item.SourcePath
            : item.ExternalId ?? string.Empty;

    public async Task<MigrationObjectStat?> StatAsync(Migration migration, string sourceKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceKey);

        var config = MigrationS3ConfigCodec.Read(migration.SourceConfig, secretProtector);
        using var client = BuildClient(config);

        try
        {
            var stat = await client.StatObjectAsync(
                new StatObjectArgs().WithBucket(config.Bucket).WithObject(sourceKey), ct);
            return new MigrationObjectStat(
                stat.Size,
                string.IsNullOrEmpty(stat.ContentType) ? "application/octet-stream" : stat.ContentType,
                stat.ETag ?? string.Empty);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (BucketNotFoundException)
        {
            return null;
        }
    }

    public async Task<Stream> DownloadAsync(Migration migration, string sourceKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceKey);

        var config = MigrationS3ConfigCodec.Read(migration.SourceConfig, secretProtector);
        using var client = BuildClient(config);

        var buffer = new MemoryStream();
        var getArgs = new GetObjectArgs()
            .WithBucket(config.Bucket)
            .WithObject(sourceKey)
            .WithCallbackStream(async (stream, innerCt) =>
            {
                await stream.CopyToAsync(buffer, innerCt);
            });

        try
        {
            await client.GetObjectAsync(getArgs, ct);
        }
        catch
        {
            await buffer.DisposeAsync();
            throw;
        }

        buffer.Position = 0;
        return buffer;
    }

    private static IMinioClient BuildClient(S3SourceConfigDto config)
    {
        if (!Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"S3 endpoint '{config.Endpoint}' is not a valid absolute URI.");

        // MinIO SDK expects host[:port] — not a scheme. WithSSL() toggles https.
        var endpoint = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

        var builder = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(config.AccessKey, config.SecretKey);

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            builder = builder.WithSSL();

        if (!string.IsNullOrWhiteSpace(config.Region))
            builder = builder.WithRegion(config.Region);

        return builder.Build();
    }
}
