using System.Net.Sockets;
using AssetHub.Application.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.Exceptions;
using Polly;
using Polly.Registry;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// MinIO adapter with dual-client support:
/// - Internal client for server-side operations (upload, download, delete, stat)
/// - Public client for presigned URLs that browsers access directly
/// Wraps operations with a Polly resilience pipeline for retry and circuit-breaker.
/// </summary>
public class MinIOAdapter(
    IMinioClient minioClient,
    IMinioClient publicMinioClient,
    ILogger<MinIOAdapter> logger,
    ResiliencePipelineProvider<string> pipelineProvider,
    HybridCache cache) : IMinIOAdapter
{
    private const string StorageUnavailableMessage = "Storage service is temporarily unavailable. Please try again.";
    private readonly ResiliencePipeline _pipeline = pipelineProvider.GetPipeline("minio");
    public async Task UploadAsync(string bucketName, string objectKey, Stream data, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            await _pipeline.ExecuteAsync(async ct =>
            {
                // Reset stream position for retries
                if (data.CanSeek) data.Position = 0;

                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey)
                    .WithStreamData(data)
                    .WithObjectSize(data.Length)
                    .WithContentType(contentType);

                await minioClient.PutObjectAsync(putObjectArgs, ct);
            }, cancellationToken);
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO upload failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to upload file. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO upload for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO upload for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }


    public async Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        return await _pipeline.ExecuteAsync(async ct =>
        {
            var memoryStream = new MemoryStream();
            try
            {
                var getObjectArgs = new GetObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey)
                    .WithCallbackStream(async (stream, innerCt) =>
                    {
                        await stream.CopyToAsync(memoryStream, innerCt);
                    });

                await minioClient.GetObjectAsync(getObjectArgs, ct);
                memoryStream.Position = 0;
                return (Stream)memoryStream;
            }
            catch
            {
                await memoryStream.DisposeAsync();
                throw;
            }
        }, cancellationToken);
    }

    public async Task<byte[]> DownloadRangeAsync(string bucketName, string objectKey, long offset, int length, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async ct =>
            {
                using var memoryStream = new MemoryStream(length);

                var getObjectArgs = new GetObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey)
                    .WithOffsetAndLength(offset, length)
                    .WithCallbackStream(async stream =>
                    {
                        await stream.CopyToAsync(memoryStream, ct);
                    });

                await minioClient.GetObjectAsync(getObjectArgs, ct);
                return memoryStream.ToArray();
            }, cancellationToken);
        }
        catch (ObjectNotFoundException)
        {
            throw; // Let this propagate - it's a business logic error, not infrastructure failure
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO download range failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to download file. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO download range for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO download range for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _pipeline.ExecuteAsync(async ct =>
            {
                var removeObjectArgs = new RemoveObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey);

                await minioClient.RemoveObjectAsync(removeObjectArgs, ct);
            }, cancellationToken);
        }
        catch (ObjectNotFoundException ex)
        {
            // Object already deleted – nothing to do.
            logger.LogDebug(ex, "Object {BucketName}/{ObjectKey} not found during delete – ignoring", bucketName, objectKey);
        }
        catch (BucketNotFoundException ex)
        {
            logger.LogWarning(ex, "Bucket {BucketName} not found during delete of {ObjectKey} – ignoring", bucketName, objectKey);
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO delete failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to delete file. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO delete for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO delete for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }

    public async Task<bool> ExistsAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async ct =>
            {
                var statObjectArgs = new StatObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey);

                await minioClient.StatObjectAsync(statObjectArgs, ct);
                return true;
            }, cancellationToken);
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
        catch (BucketNotFoundException)
        {
            return false;
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO exists check failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to check file existence. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO exists check for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO exists check for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }

    public async Task<ObjectStatInfo?> StatObjectAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async ct =>
            {
                var statObjectArgs = new StatObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey);

                var stat = await minioClient.StatObjectAsync(statObjectArgs, ct);
                return new ObjectStatInfo(stat.Size, stat.ContentType, stat.ETag);
            }, cancellationToken);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (BucketNotFoundException)
        {
            return null;
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO stat failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to retrieve file metadata. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO stat for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO stat for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }

    /// <summary>
    /// Generate presigned download URL using the PUBLIC MinIO client,
    /// so the URL is accessible from browsers.
    /// URLs are cached for 75% of their expiry time to reduce MinIO calls.
    /// </summary>
    public async Task<string> GetPresignedDownloadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, bool forceDownload = false, string? downloadFileName = null, CancellationToken cancellationToken = default)
    {
        // Build cache key from all parameters that affect the URL
        var sanitizedFileName = forceDownload ? SanitizeFileName(downloadFileName ?? Path.GetFileName(objectKey)) : null;
        var cacheKey = $"presigned:{bucketName}:{objectKey}:{forceDownload}:{sanitizedFileName}";

        // Cache for 75% of expiry time to ensure URL is still valid when served
        var cacheDuration = TimeSpan.FromSeconds(expirySeconds * 0.75);
        var cacheOptions = new HybridCacheEntryOptions
        {
            Expiration = cacheDuration,
            LocalCacheExpiration = cacheDuration
        };

        try
        {
            return await cache.GetOrCreateAsync(cacheKey, async ct =>
            {
                return await _pipeline.ExecuteAsync(async _ =>
                {
                    var presignedGetObjectArgs = new PresignedGetObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(objectKey)
                        .WithExpiry(expirySeconds);

                    if (forceDownload && sanitizedFileName is not null)
                    {
                        var headers = new Dictionary<string, string>
                        {
                            ["response-content-disposition"] = $"attachment; filename=\"{sanitizedFileName}\""
                        };
                        presignedGetObjectArgs.WithHeaders(headers);
                    }

                    return await publicMinioClient.PresignedGetObjectAsync(presignedGetObjectArgs);
                }, ct);
            }, cacheOptions, cancellationToken: cancellationToken);
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO presigned URL generation failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to generate download URL. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO presigned URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO presigned URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException ex)
        {
            logger.LogError(ex, "Circuit breaker open for MinIO presigned URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }

    /// <summary>
    /// Generate presigned download URL using the INTERNAL MinIO client.
    /// Used by server-side tools (FFmpeg) that need HTTP access to objects
    /// without downloading the entire file to disk first.
    /// Not cached — these are short-lived, single-use URLs.
    /// </summary>
    public async Task<string> GetInternalPresignedDownloadUrlAsync(string bucketName, string objectKey, int expirySeconds = 600, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async _ =>
            {
                var args = new PresignedGetObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey)
                    .WithExpiry(expirySeconds);

                return await minioClient.PresignedGetObjectAsync(args);
            }, cancellationToken);
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO internal presigned URL generation failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to generate internal download URL.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO internal presigned URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO internal presigned URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }

    /// <summary>
    /// Strips control characters (including CRLF) and quote characters from a filename
    /// to prevent Content-Disposition header injection.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var sb = new System.Text.StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            if (c is '\r' or '\n' or '"' or '\\' || c < 0x20)
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Generate presigned upload (PUT) URL using the PUBLIC MinIO client,
    /// so browsers can upload directly to MinIO.
    /// </summary>
    public async Task<string> GetPresignedUploadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureBucketExistsAsync(bucketName, cancellationToken);

            var url = await _pipeline.ExecuteAsync(async _ =>
            {
                var presignedPutObjectArgs = new PresignedPutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey)
                    .WithExpiry(expirySeconds);

                return await publicMinioClient.PresignedPutObjectAsync(presignedPutObjectArgs);
            }, cancellationToken);
            return url;
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO presigned upload URL generation failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to generate upload URL. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO presigned upload URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO presigned upload URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _pipeline.ExecuteAsync(async ct =>
            {
                var bucketExistsArgs = new BucketExistsArgs()
                    .WithBucket(bucketName);

                bool exists = await minioClient.BucketExistsAsync(bucketExistsArgs, ct);

                if (!exists)
                {
                    logger.LogInformation("Creating bucket: {BucketName}", bucketName);
                    var makeBucketArgs = new MakeBucketArgs()
                        .WithBucket(bucketName);

                    await minioClient.MakeBucketAsync(makeBucketArgs, ct);
                }
            }, cancellationToken);
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO bucket check/creation failed for {BucketName}", bucketName);
            throw new StorageException("Storage service failed during bucket setup. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO bucket check for {BucketName}", bucketName);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO bucket check for {BucketName}", bucketName);
            throw new StorageException(StorageUnavailableMessage, ex);
        }
    }
}
