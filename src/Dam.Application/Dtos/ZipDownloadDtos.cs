namespace Dam.Application.Dtos;

/// <summary>
/// Response returned when a ZIP download is enqueued.
/// </summary>
public record ZipDownloadEnqueuedResponse
{
    /// <summary>Unique ID to poll for status.</summary>
    public Guid JobId { get; init; }

    /// <summary>URL to poll for build status (relative).</summary>
    public string StatusUrl { get; init; } = "";

    /// <summary>Human-readable message.</summary>
    public string Message { get; init; } = "";
}

/// <summary>
/// Response returned when polling for ZIP download status.
/// </summary>
public record ZipDownloadStatusResponse
{
    /// <summary>Job ID.</summary>
    public Guid JobId { get; init; }

    /// <summary>Build status: pending, building, completed, failed.</summary>
    public string Status { get; init; } = "";

    /// <summary>Presigned download URL (only set when status == completed).</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>Filename of the ZIP archive.</summary>
    public string FileName { get; init; } = "";

    /// <summary>Number of assets in the ZIP.</summary>
    public int AssetCount { get; init; }

    /// <summary>Total ZIP size in bytes (only set when completed).</summary>
    public long? SizeBytes { get; init; }

    /// <summary>When the download link expires (only set when completed).</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Error message (only set when status == failed).</summary>
    public string? Error { get; init; }
}
