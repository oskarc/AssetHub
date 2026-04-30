namespace AssetHub.Application.Messages;

// ── Commands (sent to specific consumers) ────────────────────────────────

public record ProcessImageCommand
{
    public Guid AssetId { get; init; }
    public string OriginalObjectKey { get; init; } = string.Empty;
    public bool SkipMetadataExtraction { get; init; }
}

public record ProcessVideoCommand
{
    public Guid AssetId { get; init; }
    public string OriginalObjectKey { get; init; } = string.Empty;
}

public record ProcessAudioCommand
{
    public Guid AssetId { get; init; }
    public string OriginalObjectKey { get; init; } = string.Empty;
}

public record BuildZipCommand
{
    public Guid ZipDownloadId { get; init; }
}

public record ApplyExportPresetsCommand
{
    public Guid SourceAssetId { get; init; }
    public List<Guid> PresetIds { get; init; } = new();
    public string RequestedByUserId { get; init; } = string.Empty;
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

    // Audio-only fields populated by ProcessAudioHandler. Image / video paths
    // leave them null and the completion handler writes nothing for those.
    public int? DurationSeconds { get; init; }
    public int? AudioBitrateKbps { get; init; }
    public int? AudioSampleRateHz { get; init; }
    public int? AudioChannels { get; init; }
    public string? WaveformPeaksPath { get; init; }
}

public record AssetProcessingFailedEvent
{
    public Guid AssetId { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string ErrorType { get; init; } = string.Empty;
    public string AssetType { get; init; } = string.Empty;
}
