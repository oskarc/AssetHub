namespace AssetHub.Application.Services;

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
    /// Get object metadata (size, content type, etag) without downloading the content.
    /// Returns null if the object does not exist.
    /// </summary>
    Task<ObjectStatInfo?> StatObjectAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a presigned URL for downloading an object.
    /// Uses the public MinIO endpoint so browsers can access it directly.
    /// When forceDownload is true, the URL will include response-content-disposition: attachment
    /// to force the browser to download instead of displaying inline.
    /// </summary>
    Task<string> GetPresignedDownloadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, bool forceDownload = false, string? downloadFileName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a presigned URL for uploading (PUT) an object.
    /// Uses the public MinIO endpoint so browsers can upload directly.
    /// </summary>
    Task<string> GetPresignedUploadUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure a bucket exists, creating it if necessary.
    /// </summary>
    Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default);
}

/// <summary>
/// MinIO object metadata returned by StatObject.
/// </summary>
public record ObjectStatInfo(long Size, string ContentType, string ETag);
