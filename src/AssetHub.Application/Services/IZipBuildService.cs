using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Manages queued ZIP download builds via Hangfire.
/// Enqueues build jobs, tracks status, and serves completed downloads.
/// </summary>
public interface IZipBuildService
{
    /// <summary>
    /// Enqueue a ZIP build for a collection (authenticated user).
    /// Returns the job tracking ID immediately.
    /// </summary>
    Task<ServiceResult<ZipDownloadEnqueuedResponse>> EnqueueCollectionZipAsync(
        Guid collectionId, string userId, CancellationToken ct);

    /// <summary>
    /// Enqueue a ZIP build for a shared collection (anonymous, token-based).
    /// Returns the job tracking ID immediately.
    /// </summary>
    Task<ServiceResult<ZipDownloadEnqueuedResponse>> EnqueueShareZipAsync(
        Guid collectionId, string shareTokenHash, string collectionName, CancellationToken ct);

    /// <summary>
    /// Get the current status of a ZIP build job.
    /// Returns a presigned download URL when the build is complete.
    /// </summary>
    Task<ServiceResult<ZipDownloadStatusResponse>> GetStatusAsync(
        Guid jobId, string? userId, string? shareTokenHash, CancellationToken ct);

    /// <summary>
    /// Hangfire background job entry point: builds the ZIP and uploads to MinIO.
    /// Do not call directly — invoked by Hangfire.
    /// </summary>
    Task BuildZipAsync(Guid zipDownloadId, CancellationToken ct);

    /// <summary>
    /// Cleanup expired ZIP downloads from MinIO and the database.
    /// Called periodically by Hangfire.
    /// </summary>
    Task CleanupExpiredAsync(CancellationToken ct);
}
