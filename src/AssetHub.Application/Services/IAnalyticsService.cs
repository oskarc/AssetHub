using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Read-side analytics queries served from the rollup tables (T5-ANL-01).
/// All methods are admin-only at the endpoint layer; this interface assumes
/// authorisation has already happened.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>
    /// Top assets by download count over the window. Ordered desc by count.
    /// Soft-deleted assets are included with <c>IsDeleted = true</c> so the
    /// admin can still see "this leaked asset was downloaded N times before
    /// it was removed."
    /// </summary>
    Task<ServiceResult<IReadOnlyList<AnalyticsAssetDownloadRowDto>>> GetTopDownloadedAssetsAsync(
        int windowDays, int take, CancellationToken ct);

    /// <summary>Daily total download count over the window — drives the trend chart.</summary>
    Task<ServiceResult<IReadOnlyList<AnalyticsDailyPointDto>>> GetDailyDownloadCountsAsync(
        int windowDays, CancellationToken ct);

    /// <summary>
    /// Bytes per collection on the most recent rollup day. Returns the top
    /// <paramref name="take"/> collections by size.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<AnalyticsStorageByCollectionRowDto>>> GetStorageByCollectionAsync(
        int take, CancellationToken ct);

    /// <summary>Bytes per asset-type on the most recent rollup day.</summary>
    Task<ServiceResult<IReadOnlyList<AnalyticsStorageByAssetTypeRowDto>>> GetStorageByAssetTypeAsync(
        CancellationToken ct);

    /// <summary>Top recipients by watermarked-download count over the window. Hash-grouped; PII reveal is a separate call.</summary>
    Task<ServiceResult<IReadOnlyList<AnalyticsExposureRowDto>>> GetTopRecipientsAsync(
        int windowDays, int take, CancellationToken ct);

    /// <summary>
    /// Reveal the plaintext recipient identifier (Keycloak sub or email) for
    /// a single hash. Audited via <c>analytics.exposure_revealed</c> with
    /// the hash + count only — never the decrypted value.
    /// </summary>
    Task<ServiceResult<RevealRecipientResponseDto>> RevealRecipientAsync(
        RevealRecipientRequestDto request, CancellationToken ct);
}

/// <summary>
/// Write-side analytics rollup operations (T5-ANL-01). Called by the
/// <c>AnalyticsRollupBackgroundService</c> in the worker. Idempotent on
/// (date, metric, entity-id) so re-running for the same day is safe.
/// </summary>
public interface IAnalyticsRollupService
{
    /// <summary>
    /// Roll up a single UTC day. Reads <c>asset.downloaded</c> /
    /// <c>collection.downloaded</c> audit events + <c>WatermarkDownload</c>
    /// rows + current asset sizes; upserts rows into both rollup tables.
    /// Returns the row counts written for telemetry.
    /// </summary>
    Task<ServiceResult<RollupResult>> RollupDayAsync(DateOnly day, CancellationToken ct);
}

/// <summary>Telemetry from a single rollup tick.</summary>
public sealed record RollupResult(
    DateOnly Day,
    int DownloadRowsWritten,
    int ExposureRowsWritten,
    int StorageRowsWritten);

/// <summary>
/// Lifecycle operations for an analytics PDF export (T5-ANL-01). The API
/// enqueues a job; the worker handler renders + uploads; the API polls
/// status. Mirrors the ZIP-download lifecycle.
/// </summary>
public interface IAnalyticsPdfJobService
{
    /// <summary>Create a pending job + dispatch the build command. Returns the new job's ID.</summary>
    Task<ServiceResult<AnalyticsPdfJobStatusDto>> EnqueueAsync(int windowDays, CancellationToken ct);

    /// <summary>
    /// Get the current status. When the job is <c>Ready</c>, returns a
    /// pre-signed MinIO download URL valid for 1 hour. Returns 404 if the
    /// caller is not the requester (admin self-scope) or the job is gone.
    /// </summary>
    Task<ServiceResult<AnalyticsPdfJobStatusDto>> GetStatusAsync(Guid jobId, CancellationToken ct);
}
