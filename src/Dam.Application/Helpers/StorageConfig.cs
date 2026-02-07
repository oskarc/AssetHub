using Microsoft.Extensions.Configuration;

namespace Dam.Application.Helpers;

/// <summary>
/// Centralized storage configuration access.
/// Provides consistent bucket name resolution across all endpoint handlers.
/// </summary>
public static class StorageConfig
{
    /// <summary>
    /// Gets the MinIO bucket name from configuration.
    /// Throws if not configured.
    /// </summary>
    public static string GetBucketName(IConfiguration configuration)
    {
        var bucketName = configuration["MinIO:BucketName"];
        if (string.IsNullOrWhiteSpace(bucketName))
            throw new InvalidOperationException("MinIO:BucketName is required. Check appsettings for the current environment.");
        return bucketName;
    }
}
