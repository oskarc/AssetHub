namespace Dam.Application.Services;

public interface IMinIOAdapter
{
    /// <summary>
    /// Upload a stream to MinIO with the given object key.
    /// </summary>
    Task UploadAsync(string bucketName, string objectKey, Stream data, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download an object from MinIO.
    /// </summary>
    Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an object from MinIO.
    /// </summary>
    Task DeleteAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if an object exists in MinIO.
    /// </summary>
    Task<bool> ExistsAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a presigned URL for downloading an object.
    /// </summary>
    Task<string> GetPresignedDownloadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure a bucket exists, creating it if necessary.
    /// </summary>
    Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default);
}
