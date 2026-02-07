using Microsoft.Extensions.Configuration;

namespace Dam.Application.Helpers;

/// <summary>
/// Centralized storage configuration access.
/// Provides consistent bucket name resolution across all endpoint handlers.
/// </summary>
public static class StorageConfig
{
    private const string DefaultBucketName = "assethub-dev";

    /// <summary>
    /// Gets the MinIO bucket name from configuration, with a consistent default fallback.
    /// </summary>
    public static string GetBucketName(IConfiguration configuration)
    {
        return configuration["MinIO:BucketName"] ?? DefaultBucketName;
    }
}
