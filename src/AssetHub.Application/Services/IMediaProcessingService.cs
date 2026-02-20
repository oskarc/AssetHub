namespace AssetHub.Application.Services;

public interface IMediaProcessingService
{
    /// <summary>
    /// Schedule processing jobs for an asset based on its type.
    /// Returns a job ID that can be tracked.
    /// </summary>
    Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process an image: create thumbnail and medium-size versions.
    /// Called by background job processor.
    /// </summary>
    Task ProcessImageAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default);

    /// <summary>
    /// Process a video: extract poster frame and create poster/preview versions.
    /// Called by background job processor.
    /// </summary>
    Task ProcessVideoAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default);

    /// <summary>
    /// Get the status of a processing job.
    /// </summary>
    Task<(bool IsCompleted, string? Status, string? ErrorMessage)> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default);
}
