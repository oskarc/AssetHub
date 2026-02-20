namespace AssetHub.Domain.Entities;

/// <summary>
/// Tracks a queued or in-progress ZIP download build.
/// The actual ZIP file is stored as a temporary object in MinIO
/// and cleaned up after expiry.
/// </summary>
public class ZipDownload
{
    public Guid Id { get; set; }

    /// <summary>Current build status.</summary>
    public ZipDownloadStatus Status { get; set; } = ZipDownloadStatus.Pending;

    /// <summary>Hangfire background job ID.</summary>
    public string? HangfireJobId { get; set; }

    /// <summary>MinIO object key for the built ZIP file.</summary>
    public string? ZipObjectKey { get; set; }

    /// <summary>User-friendly filename for the download.</summary>
    public string ZipFileName { get; set; } = "";

    /// <summary>Scope: asset or collection.</summary>
    public ShareScopeType ScopeType { get; set; }

    /// <summary>The collection ID being downloaded.</summary>
    public Guid ScopeId { get; set; }

    /// <summary>User ID of the requester (null for anonymous share downloads).</summary>
    public string? RequestedByUserId { get; set; }

    /// <summary>Hashed share token (for anonymous share downloads).</summary>
    public string? ShareTokenHash { get; set; }

    /// <summary>Total size of the built ZIP in bytes.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Number of assets included.</summary>
    public int AssetCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>When the temporary ZIP file should be deleted from MinIO.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Error message if the build failed.</summary>
    public string? ErrorMessage { get; set; }
}
