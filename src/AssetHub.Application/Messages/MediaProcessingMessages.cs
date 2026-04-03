namespace AssetHub.Application.Messages;

// ── Commands (sent to specific consumers) ────────────────────────────────

public record ProcessImageCommand
{
    public Guid AssetId { get; init; }
    public string OriginalObjectKey { get; init; } = string.Empty;
}

public record ProcessVideoCommand
{
    public Guid AssetId { get; init; }
    public string OriginalObjectKey { get; init; } = string.Empty;
}

public record BuildZipCommand
{
    public Guid ZipDownloadId { get; init; }
}

// ── Events (published for any interested subscriber) ─────────────────────

public record AssetProcessingCompletedEvent
{
    public Guid AssetId { get; init; }
    public string? ThumbObjectKey { get; init; }
    public string? MediumObjectKey { get; init; }
    public string? PosterObjectKey { get; init; }
    public Dictionary<string, object>? MetadataJson { get; init; }
    public string? Copyright { get; init; }
}

public record AssetProcessingFailedEvent
{
    public Guid AssetId { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string ErrorType { get; init; } = string.Empty;
    public string AssetType { get; init; } = string.Empty;
}
