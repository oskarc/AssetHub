using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.Exceptions;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class S3ConnectorClient(ILogger<S3ConnectorClient> logger) : IS3ConnectorClient
{
    public async Task<IReadOnlyList<S3ObjectInfo>> ListObjectsAsync(
        S3SourceConfigDto config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        using var client = BuildClient(config);

        var listArgs = new ListObjectsArgs()
            .WithBucket(config.Bucket)
            .WithRecursive(true);
        if (!string.IsNullOrWhiteSpace(config.Prefix))
            listArgs = listArgs.WithPrefix(config.Prefix);

        var items = new List<S3ObjectInfo>();
        var completion = new TaskCompletionSource();
        using var cancelReg = ct.Register(() => completion.TrySetCanceled(ct));

        var subscription = client.ListObjectsAsync(listArgs, ct).Subscribe(
            onNext: item =>
            {
                // MinIO's Item uses ulong for Size; migration items expect long. Guard
                // against the unlikely > 8 EiB object by clamping rather than throwing.
                var size = item.Size > long.MaxValue ? long.MaxValue : (long)item.Size;
                items.Add(new S3ObjectInfo(item.Key, size, item.ETag ?? string.Empty));
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

    public async Task<ObjectStatInfo?> StatObjectAsync(
        S3SourceConfigDto config, string objectKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(objectKey);

        using var client = BuildClient(config);

        try
        {
            var stat = await client.StatObjectAsync(
                new StatObjectArgs().WithBucket(config.Bucket).WithObject(objectKey), ct);
            return new ObjectStatInfo(
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

    public async Task<Stream> DownloadObjectAsync(
        S3SourceConfigDto config, string objectKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(objectKey);

        using var client = BuildClient(config);

        var buffer = new MemoryStream();
        var getArgs = new GetObjectArgs()
            .WithBucket(config.Bucket)
            .WithObject(objectKey)
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
