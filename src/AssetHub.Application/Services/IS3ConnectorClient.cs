using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Read-only client for enumerating objects in a remote S3-compatible bucket owned
/// by a migration source. The client is built on demand from per-migration credentials;
/// it is not the same as the app's own <see cref="IMinIOAdapter"/>, which targets the
/// internal storage bucket.
/// </summary>
public interface IS3ConnectorClient
{
    /// <summary>
    /// List all objects under the configured bucket/prefix recursively. Used by the
    /// S3 migration scan handler to seed <c>MigrationItem</c> rows. Throws on
    /// transport / credential errors; callers are expected to translate these into
    /// a scan-failed outcome.
    /// </summary>
    Task<IReadOnlyList<S3ObjectInfo>> ListObjectsAsync(S3SourceConfigDto config, CancellationToken ct);
}

/// <summary>
/// Minimal metadata about an S3 object needed to create a migration item.
/// </summary>
public record S3ObjectInfo(string Key, long Size, string ETag);
