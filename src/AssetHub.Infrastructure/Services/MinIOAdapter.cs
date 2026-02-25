using System.Net.Sockets;
using AssetHub.Application.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.Exceptions;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// MinIO adapter with dual-client support:
/// - Internal client for server-side operations (upload, download, delete, stat)
/// - Public client for presigned URLs that browsers access directly
/// </summary>
public class MinIOAdapter(
    IMinioClient minioClient,
    IMinioClient publicMinioClient,
    ILogger<MinIOAdapter> logger,
    IMemoryCache cache) : IMinIOAdapter
{
    public async Task UploadAsync(string bucketName, string objectKey, Stream data, string contentType, CancellationToken cancellationToken = default)
    {
        try
        {
            // Bucket existence is guaranteed at startup via RunStartupTasksAsync; no per-call check needed.
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithStreamData(data)
                .WithObjectSize(data.Length)
                .WithContentType(contentType);

            await minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO upload failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to upload file. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO upload for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO upload for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
    }


    public async Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        // Use a pipe to stream data without buffering the entire file in memory.
        // GetObjectAsync blocks until its callback completes, so the download
        // must run on a background task; otherwise the pipe writer will stall
        // once its buffer fills because no reader is consuming yet (deadlock).
        var pipe = new System.IO.Pipelines.Pipe();

        _ = Task.Run(async () =>
        {
            Exception? completionException = null;
            try
            {
                var getObjectArgs = new GetObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectKey)
                    .WithCallbackStream(async (stream) =>
                    {
                        try
                        {
                            await stream.CopyToAsync(pipe.Writer.AsStream(), 81920, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            completionException = ex;
                        }
                    });

                await minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
            }
            catch (Exception ex)
            {
                completionException ??= ex;
            }
            finally
            {
                await pipe.Writer.CompleteAsync(completionException);
            }
        }, CancellationToken.None); // Use None: the inner code already checks cancellationToken

        return pipe.Reader.AsStream();
    }

    public async Task<byte[]> DownloadRangeAsync(string bucketName, string objectKey, long offset, int length, CancellationToken cancellationToken = default)
    {
        try
        {
            using var memoryStream = new MemoryStream(length);

            var getObjectArgs = new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithOffsetAndLength(offset, length)
                .WithCallbackStream(async stream =>
                {
                    await stream.CopyToAsync(memoryStream, cancellationToken);
                });

            await minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
            return memoryStream.ToArray();
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
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO download range for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey);

            await minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
        }
        catch (ObjectNotFoundException)
        {
            // Object already deleted – nothing to do.
            logger.LogDebug("Object {BucketName}/{ObjectKey} not found during delete – ignoring", bucketName, objectKey);
        }
        catch (BucketNotFoundException)
        {
            logger.LogWarning("Bucket {BucketName} not found during delete of {ObjectKey} – ignoring", bucketName, objectKey);
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO delete failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to delete file. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO delete for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO delete for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
    }

    public async Task<bool> ExistsAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey);

            await minioClient.StatObjectAsync(statObjectArgs, cancellationToken);
            return true;
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
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO exists check for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
    }

    public async Task<ObjectStatInfo?> StatObjectAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey);

            var stat = await minioClient.StatObjectAsync(statObjectArgs, cancellationToken);
            return new ObjectStatInfo(stat.Size, stat.ContentType, stat.ETag);
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
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO stat for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
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

        if (cache.TryGetValue(cacheKey, out string? cachedUrl) && cachedUrl is not null)
        {
            return cachedUrl;
        }

        try
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

            var url = await publicMinioClient.PresignedGetObjectAsync(presignedGetObjectArgs);

            // Cache for 75% of expiry time to ensure URL is still valid when served
            var cacheDuration = TimeSpan.FromSeconds(expirySeconds * 0.75);
            cache.Set(cacheKey, url, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = cacheDuration,
                Size = 1
            });

            return url;
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO presigned URL generation failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to generate download URL. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO presigned URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO presigned URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
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

            var presignedPutObjectArgs = new PresignedPutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithExpiry(expirySeconds);

            var url = await publicMinioClient.PresignedPutObjectAsync(presignedPutObjectArgs);
            return url;
        }
        catch (StorageException)
        {
            throw; // Already wrapped
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO presigned upload URL generation failed for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service failed to generate upload URL. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO presigned upload URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO presigned upload URL generation for {BucketName}/{ObjectKey}", bucketName, objectKey);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
    }

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        try
        {
            var bucketExistsArgs = new BucketExistsArgs()
                .WithBucket(bucketName);

            bool exists = await minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);

            if (!exists)
            {
                logger.LogInformation("Creating bucket: {BucketName}", bucketName);
                var makeBucketArgs = new MakeBucketArgs()
                    .WithBucket(bucketName);

                await minioClient.MakeBucketAsync(makeBucketArgs, cancellationToken);
            }
        }
        catch (MinioException ex)
        {
            logger.LogError(ex, "MinIO bucket check/creation failed for {BucketName}", bucketName);
            throw new StorageException("Storage service failed during bucket setup. Please try again.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during MinIO bucket check for {BucketName}", bucketName);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
        catch (SocketException ex)
        {
            logger.LogError(ex, "Connection error during MinIO bucket check for {BucketName}", bucketName);
            throw new StorageException("Storage service is temporarily unavailable. Please try again.", ex);
        }
    }
}
