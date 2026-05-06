namespace AssetHub.Application.Dtos;

/// <summary>
/// Result returned by the admin watermark verify endpoint (T5-WMK-01). PII
/// (<see cref="RecipientUserId"/>, <see cref="RecipientEmail"/>) is decrypted only
/// at this endpoint and never written to the audit log — see
/// <c>watermark.verified</c> audit row, which records only <c>(assetId, downloadToken)</c>.
/// </summary>
public sealed record WatermarkVerificationResultDto
{
    /// <summary>True when at least one watermark layer extracted with non-zero confidence.</summary>
    public bool Matched { get; init; }

    /// <summary>The asset whose fingerprint matched, when the asset layer extracted.</summary>
    public Guid? AssetId { get; init; }

    /// <summary>The recipient-layer download token, when the recipient layer extracted.</summary>
    public Guid? DownloadToken { get; init; }

    /// <summary>Decrypted Keycloak sub for internal-user downloads. Null when not applicable or unavailable.</summary>
    public string? RecipientUserId { get; init; }

    /// <summary>Decrypted email for share-recipient downloads. Null when not applicable or unavailable.</summary>
    public string? RecipientEmail { get; init; }

    /// <summary>The share that served the download. Null for internal-app downloads.</summary>
    public Guid? ShareId { get; init; }

    /// <summary>When the download happened, when a row matched.</summary>
    public DateTime? DownloadedAt { get; init; }

    /// <summary>"raw" / "medium" / "thumb" / "zip" / "share" — the path the bytes flowed through.</summary>
    public string? DownloadPath { get; init; }

    /// <summary>Confidence the asset-fingerprint layer extracted with: "high" / "medium" / "low" / "no_match".</summary>
    public string AssetLayerConfidence { get; init; } = "no_match";

    /// <summary>Confidence the recipient-fingerprint layer extracted with: "high" / "medium" / "low" / "no_match".</summary>
    public string RecipientLayerConfidence { get; init; } = "no_match";

    /// <summary>
    /// Free-text human-readable reason — e.g. "asset no longer in library",
    /// "recipient layer not recoverable", or null on a clean attribution.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>Body of <c>PATCH /api/v1/collections/{id}/watermark</c>.</summary>
public sealed record UpdateCollectionWatermarkDto
{
    public bool Enabled { get; init; }
}

/// <summary>
/// Body of <c>PATCH /api/v1/assets/{id}/watermark</c>. Null clears the override
/// (asset returns to inheriting from its collection).
/// </summary>
public sealed record UpdateAssetWatermarkDto
{
    public bool? Override { get; init; }
}

/// <summary>
/// Body of <c>PATCH /api/v1/shares/{id}/watermark</c>. Null clears the override
/// (share returns to inheriting from asset / collection).
/// </summary>
public sealed record UpdateShareWatermarkDto
{
    public bool? Override { get; init; }
}

/// <summary>
/// Discriminated result of <c>IAssetQueryService.ResolveRenditionDownloadAsync</c> (T5-WMK-01).
/// Exactly one of <see cref="PresignedUrl"/> and <see cref="Stream"/> is non-null:
/// <list type="bullet">
///   <item><see cref="PresignedUrl"/> set → endpoint 302-redirects (un-watermarked, CDN-fast path)</item>
///   <item><see cref="Stream"/> set → endpoint streams through with the watermarked bytes</item>
/// </list>
/// </summary>
public sealed record RenditionDownloadResult
{
    public string? PresignedUrl { get; init; }
    public RenditionStreamPayload? Stream { get; init; }
}

/// <summary>Watermarked rendition bytes plus the metadata the endpoint needs to set <c>Content-Disposition</c>.</summary>
public sealed record RenditionStreamPayload
{
    public required Stream Content { get; init; }
    public required string ContentType { get; init; }
    public string? DownloadFileName { get; init; }
    public bool ForceDownload { get; init; }
}
