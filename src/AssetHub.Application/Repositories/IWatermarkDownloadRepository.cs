using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Read / write access to <c>WatermarkDownloads</c> rows (T5-WMK-01). Looking up by
/// token is the verify-endpoint hot path and is cached via <c>HybridCache</c>
/// (24 h TTL, tag-invalidated on the rare admin-purge path).
/// </summary>
public interface IWatermarkDownloadRepository
{
    /// <summary>Persist a new <c>WatermarkDownload</c> row.</summary>
    Task AddAsync(WatermarkDownload download, CancellationToken ct);

    /// <summary>
    /// Fetch a row by its token. Returns null if no match — that's a legitimate
    /// "no match" outcome, not an error.
    /// </summary>
    Task<WatermarkDownload?> GetByTokenAsync(Guid downloadToken, CancellationToken ct);

    /// <summary>
    /// Recent downloads of an asset, newest first. Used by T5-ANL-01 to build
    /// "downloads by asset" / "exposure" views without further schema work.
    /// </summary>
    Task<IReadOnlyList<WatermarkDownload>> GetRecentForAssetAsync(
        Guid assetId,
        int take,
        CancellationToken ct);

    /// <summary>
    /// Lookups by recipient hash — answers "all downloads attributable to this user".
    /// Used by T5-ANL-01 dashboards and by GDPR subject-access-request flows.
    /// </summary>
    Task<IReadOnlyList<WatermarkDownload>> GetByRecipientUserHashAsync(
        byte[] recipientUserIdHash,
        int take,
        CancellationToken ct);
}
