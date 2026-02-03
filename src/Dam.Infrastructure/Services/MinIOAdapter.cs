using Dam.Application.Services;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.Exceptions;

namespace Dam.Infrastructure.Services;

public class MinIOAdapter(
    IMinioClient minioClient,
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
            logger.LogWarning(ex, "Error checking if object exists: {Bucket}/{Key}", bucketName, objectKey);
            throw;
        }
    }

    public async Task<string> GetPresignedDownloadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, CancellationToken cancellationToken = default)
    {
        var presignedGetObjectArgs = new PresignedGetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithExpiry(expirySeconds);

        var url = await minioClient.PresignedGetObjectAsync(presignedGetObjectArgs);
        return url;
    }

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
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
}
