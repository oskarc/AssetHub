using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// One row of the "Top downloaded assets" panel (T5-ANL-01).
/// </summary>
public sealed record AnalyticsAssetDownloadRowDto
{
    public required Guid AssetId { get; init; }
    public string? Title { get; init; }
    public string? AssetType { get; init; }
    public required long DownloadCount { get; init; }
    /// <summary>True when the asset has been soft-deleted — UI shows "(deleted)" and disables the row link.</summary>
    public bool IsDeleted { get; init; }
}

/// <summary>One row of the "Storage by collection" sub-panel.</summary>
public sealed record AnalyticsStorageByCollectionRowDto
{
    public required Guid CollectionId { get; init; }
    public string? CollectionName { get; init; }
    public required long Bytes { get; init; }
    public required int AssetCount { get; init; }
}

/// <summary>One row of the "Storage by asset type" sub-panel.</summary>
public sealed record AnalyticsStorageByAssetTypeRowDto
{
    public required string AssetType { get; init; }
    public required long Bytes { get; init; }
    public required int AssetCount { get; init; }
}

/// <summary>
/// One row of the "Forensic exposure" panel — top recipients by watermarked
/// download count. The recipient is identified only by HMAC hash on the
/// list endpoint; admins reveal the plaintext via a separate endpoint that
/// audits the reveal (G-12b carries forward from T5-WMK-01).
/// </summary>
public sealed record AnalyticsExposureRowDto
{
    /// <summary>
    /// 64-char base64 of the recipient's HMAC hash (matches
    /// <c>WatermarkDownload.RecipientUserIdHash</c> / <c>RecipientEmailHash</c>).
    /// Sufficient to group; not reversible to PII.
    /// </summary>
    public required string RecipientHash { get; init; }

    /// <summary>"user" for internal-app downloads, "email" for share recipients.</summary>
    public required string RecipientKind { get; init; }

    /// <summary>Total watermarked downloads for this recipient in the window.</summary>
    public required long DownloadCount { get; init; }

    /// <summary>Number of distinct assets this recipient has touched (helps prioritise reveal).</summary>
    public required int DistinctAssetCount { get; init; }
}

/// <summary>One point on the dashboard "downloads per day" trend chart.</summary>
public sealed record AnalyticsDailyPointDto
{
    public required DateOnly Date { get; init; }
    public required long Count { get; init; }
}

/// <summary>Body of <c>POST /api/v1/admin/analytics/exposure/reveal</c>.</summary>
public sealed record RevealRecipientRequestDto
{
    [Required]
    [StringLength(128, MinimumLength = 8)]
    public required string RecipientHash { get; init; }

    /// <summary>"user" or "email" — which hash column the value lives on.</summary>
    [Required]
    [RegularExpression("^(user|email)$")]
    public required string RecipientKind { get; init; }
}

/// <summary>
/// Result of a recipient-reveal call. <see cref="RecipientUserId"/> is set
/// for "user" kind, <see cref="RecipientEmail"/> for "email" kind. When key
/// rotation has made decryption impossible, both are null and
/// <see cref="DecryptionFailed"/> is true.
/// </summary>
public sealed record RevealRecipientResponseDto
{
    public string? RecipientUserId { get; init; }
    public string? RecipientEmail { get; init; }
    public bool DecryptionFailed { get; init; }
}

/// <summary>Status of an in-flight analytics PDF export.</summary>
public sealed record AnalyticsPdfJobStatusDto
{
    public required Guid JobId { get; init; }
    /// <summary>"pending" | "building" | "ready" | "failed".</summary>
    public required string Status { get; init; }
    public required int WindowDays { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    /// <summary>Pre-signed MinIO download URL — populated only when <see cref="Status"/> is "ready".</summary>
    public string? DownloadUrl { get; init; }
    public string? ErrorMessage { get; init; }
}
