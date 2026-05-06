namespace AssetHub.Application.Services.Watermarking;

/// <summary>
/// Orchestrates on-the-fly watermark application for download paths (T5-WMK-01). Resolves
/// the effective watermark setting (Share → Asset → Collection precedence), composes
/// <see cref="IWatermarkEmbedder"/> calls per the no-gap rule, persists the resulting
/// <c>WatermarkDownload</c> row, and emits the <c>watermark.applied_on_download</c>
/// audit event.
/// </summary>
public interface IWatermarkService
{
    /// <summary>
    /// Apply watermarking to an outgoing download stream. When watermarking is effectively
    /// off for the asset (per inheritance precedence), the original stream is returned
    /// unchanged and no row is written. Otherwise both layers are embedded — the asset
    /// layer is skipped if <see cref="WatermarkContext.AssetFingerprintAlreadyApplied"/>
    /// is true (the stored renditions already carry it) — a <c>WatermarkDownload</c> row
    /// is persisted, and the watermarked stream is returned.
    /// </summary>
    Task<ServiceResult<WatermarkedDownloadResult>> WatermarkForDownloadAsync(
        Stream input,
        WatermarkContext context,
        CancellationToken ct);

    /// <summary>
    /// Resolve whether watermarking is effectively on for a given (asset, share) pair
    /// without applying it. Used by ZIP builders to decide per-asset whether to invoke
    /// the embedder or skip; also used by the share UI to decide whether to render the
    /// disclosure notice.
    /// </summary>
    Task<ServiceResult<bool>> IsWatermarkingEffectiveAsync(
        Guid assetId,
        Guid? shareId,
        CancellationToken ct);

    /// <summary>
    /// Set <c>Collection.WatermarkEnabled</c>. Caller is responsible for authorization
    /// (manager-or-above on the collection); this method writes + audits only.
    /// </summary>
    Task<ServiceResult> SetCollectionWatermarkAsync(
        Guid collectionId, bool enabled, CancellationToken ct);

    /// <summary>
    /// Set <c>Asset.WatermarkOverride</c>. <paramref name="override"/> = null clears
    /// the override (asset returns to inheriting from its collection).
    /// </summary>
    Task<ServiceResult> SetAssetWatermarkOverrideAsync(
        Guid assetId, bool? @override, CancellationToken ct);

    /// <summary>
    /// Set <c>Share.WatermarkOverride</c>. <paramref name="override"/> = null clears
    /// the override (share returns to inheriting from asset / collection).
    /// </summary>
    Task<ServiceResult> SetShareWatermarkOverrideAsync(
        Guid shareId, bool? @override, CancellationToken ct);
}

/// <summary>
/// Input to <see cref="IWatermarkService.WatermarkForDownloadAsync"/>. Carries everything
/// the service needs to resolve precedence, build the recipient-fingerprint payload, and
/// persist the audit row. Internal contract type — not endpoint-facing.
/// </summary>
public sealed record WatermarkContext
{
    public required Guid AssetId { get; init; }
    public required string DownloadPath { get; init; } // "raw" | "medium" | "thumb" | "zip" | "share"
    public Guid? ShareId { get; init; }

    /// <summary>Plaintext Keycloak sub for internal-user downloads. Null for share-recipient downloads.</summary>
    public string? RecipientUserId { get; init; }

    /// <summary>Plaintext email for share-recipient downloads. Null for internal-user or anonymous-share downloads.</summary>
    public string? RecipientEmail { get; init; }

}

/// <summary>Output of a successful watermark application.</summary>
public sealed record WatermarkedDownloadResult
{
    /// <summary>The watermarked stream the endpoint should serve to the client.</summary>
    public required Stream Output { get; init; }

    /// <summary>The opaque download token persisted on the new <c>WatermarkDownload</c> row.</summary>
    public required Guid DownloadToken { get; init; }

    /// <summary>
    /// True when both layers were embedded, false when watermarking was effectively off
    /// for this download (in which case <see cref="Output"/> is the unchanged input).
    /// </summary>
    public required bool WasWatermarked { get; init; }
}
