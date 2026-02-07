using Dam.Application.Services;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.Exceptions;

namespace Dam.Infrastructure.Services;

/// <summary>
/// MinIO adapter with dual-client support:
/// - Internal client for server-side operations (upload, download, delete, stat)
/// - Public client for presigned URLs that browsers access directly
/// </summary>
public class MinIOAdapter(
    IMinioClient minioClient,
    IMinioClient publicMinioClient,
    ILogger<MinIOAdapter> logger) : IMinIOAdapter
{
    public async Task UploadAsync(string bucketName, string objectKey, Stream data, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType);

        await minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
    }

    public async Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();
        
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithCallbackStream(async (stream) =>
            {
                await stream.CopyToAsync(memoryStream, cancellationToken);
            });

        await minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
        memoryStream.Position = 0;
        
        return memoryStream;
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey);

        await minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, $"Error checking if object exists: {bucketName}/{objectKey}");
            throw;
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
    }

    /// <summary>
    /// Generate presigned download URL using the PUBLIC MinIO client,
    /// so the URL is accessible from browsers.
    /// </summary>
    public async Task<string> GetPresignedDownloadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, CancellationToken cancellationToken = default)
    {
        var presignedGetObjectArgs = new PresignedGetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithExpiry(expirySeconds);

        var url = await publicMinioClient.PresignedGetObjectAsync(presignedGetObjectArgs);
        return url;
    }

    /// <summary>
    /// Generate presigned upload (PUT) URL using the PUBLIC MinIO client,
    /// so browsers can upload directly to MinIO.
    /// </summary>
    public async Task<string> GetPresignedUploadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        var presignedPutObjectArgs = new PresignedPutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithExpiry(expirySeconds);

        var url = await publicMinioClient.PresignedPutObjectAsync(presignedPutObjectArgs);
        return url;
    }

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var bucketExistsArgs = new BucketExistsArgs()
            .WithBucket(bucketName);

        bool exists = await minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);

        if (!exists)
        {
            logger.LogInformation($"Creating bucket: {bucketName}");
            var makeBucketArgs = new MakeBucketArgs()
                .WithBucket(bucketName);

            await minioClient.MakeBucketAsync(makeBucketArgs, cancellationToken);
        }
    }
}
