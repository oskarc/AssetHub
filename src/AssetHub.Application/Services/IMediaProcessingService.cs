namespace AssetHub.Application.Services;

public interface IMediaProcessingService
{
    /// <summary>
    /// Schedule processing jobs for an asset based on its type.
    /// Returns a job ID that can be tracked.
    /// </summary>
    Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule processing jobs for an asset based on its type, optionally skipping metadata extraction.
    /// </summary>
    Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, bool skipMetadata, CancellationToken cancellationToken = default);
}
