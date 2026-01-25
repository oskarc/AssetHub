using Dam.Application.Services;
using Minio;

namespace Dam.Infrastructure.Services;

public class MinIOAdapter : IMinIOAdapter
{
    private readonly IMinioClient _minioClient;

    public MinIOAdapter(IMinioClient minioClient)
    {
        _minioClient = minioClient;
    }

    public async Task UploadAsync(string bucketName, string objectKey, Stream data, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType);

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
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

        await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
        memoryStream.Position = 0;
        
        return memoryStream;
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey);

        await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey);

            await _minioClient.StatObjectAsync(statObjectArgs, cancellationToken);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<string> GetPresignedDownloadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, CancellationToken cancellationToken = default)
    {
        var presignedGetObjectArgs = new PresignedGetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithExpiry(expirySeconds);

        var url = await _minioClient.PresignedGetObjectAsync(presignedGetObjectArgs);
        return url;
    }

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var bucketExistsArgs = new BucketExistsArgs()
            .WithBucket(bucketName);

        bool exists = await _minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);

        if (!exists)
        {
            var makeBucketArgs = new MakeBucketArgs()
                .WithBucket(bucketName);

            await _minioClient.MakeBucketAsync(makeBucketArgs, cancellationToken);
        }
    }
}
