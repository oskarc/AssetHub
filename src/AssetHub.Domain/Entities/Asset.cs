namespace AssetHub.Domain.Entities;

public class Asset
{
    public Guid Id { get; set; }
    public AssetType AssetType { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Processing;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Copyright { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> MetadataJson { get; set; } = new();

    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }

    public string OriginalObjectKey { get; set; } = string.Empty;
    public string? ThumbObjectKey { get; set; }
    public string? MediumObjectKey { get; set; }
    public string? PosterObjectKey { get; set; }

    /// <summary>
    /// Duration of the asset in seconds (audio + video). Null for assets that
    /// have not been probed yet or whose type doesn't carry duration.
    /// </summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Audio bitrate in kbps. Populated by the audio worker handler.</summary>
    public int? AudioBitrateKbps { get; set; }

    /// <summary>Audio sample rate in Hz. Populated by the audio worker handler.</summary>
    public int? AudioSampleRateHz { get; set; }

    /// <summary>Audio channel count. Populated by the audio worker handler.</summary>
    public int? AudioChannels { get; set; }

    /// <summary>
    /// MinIO object key of the precomputed waveform peaks JSON for audio assets.
    /// Generated once during initial processing; immutable for the asset's life.
    /// </summary>
    public string? WaveformPeaksPath { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// When non-null, the asset is soft-deleted and lives in Trash. A background worker
    /// purges rows where DeletedAt is older than AssetLifecycleSettings.TrashRetentionDays.
    /// Queries filter on DeletedAt IS NULL via a global EF query filter; admin-trash endpoints
    /// use IgnoreQueryFilters() to see soft-deleted rows.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Who soft-deleted the asset. Null for never-deleted assets.</summary>
    public string? DeletedByUserId { get; set; }

    /// <summary>
    /// Current version number. Starts at 1; bumps each time a new AssetVersion is captured
    /// (e.g., the image-editor Replace flow). The Asset row always holds the current state;
    /// historical versions live in AssetVersions.
    /// </summary>
    public int CurrentVersionNumber { get; set; } = 1;

    /// <summary>Historical versions captured before each Replace. Ordered by VersionNumber.</summary>
    public ICollection<AssetVersion> Versions { get; set; } = new List<AssetVersion>();

    /// <summary>
    /// Publishing-workflow state (T3-WF-01). Defaults to Published for
    /// backward compatibility with existing flows that expect new uploads to
    /// be immediately shareable — admins who want a review workflow flip
    /// <c>WorkflowSettings.NewAssetState</c> to Draft in config.
    /// </summary>
    public AssetWorkflowState WorkflowState { get; set; } = AssetWorkflowState.Published;

    /// <summary>
    /// Stamp of the last workflow transition. Used by the panel to show
    /// "approved 3 days ago" / "in review since…" without re-querying the
    /// transitions table.
    /// </summary>
    public DateTime? WorkflowStateUpdatedAt { get; set; }

    /// <summary>
    /// References the source asset this was derived from (via image editing / export presets).
    /// Null for original assets.
    /// </summary>
    public Guid? SourceAssetId { get; set; }
    public Asset? SourceAsset { get; set; }

    /// <summary>
    /// Assets derived from this one (edited copies, preset exports).
    /// </summary>
    public ICollection<Asset> Derivatives { get; set; } = new List<Asset>();

    /// <summary>
    /// Persisted edit document (fabric.js layer JSON) for re-opening edits.
    /// Stored as JSONB. Versioned: {"v":1,"layers":[...],"canvas":{...}}
    /// </summary>
    public string? EditDocument { get; set; }

    /// <summary>
    /// All collections this asset belongs to. All collections are equal - no hierarchy.
    /// </summary>
    public ICollection<AssetCollection> AssetCollections { get; set; } = new List<AssetCollection>();

    /// <summary>
    /// Mark asset as ready after processing completes.
    /// </summary>
    public void MarkReady(string? thumbKey = null, string? mediumKey = null, string? posterKey = null)
    {
        Status = AssetStatus.Ready;
        UpdatedAt = DateTime.UtcNow;
        if (thumbKey is not null) ThumbObjectKey = thumbKey;
        if (mediumKey is not null) MediumObjectKey = mediumKey;
        if (posterKey is not null) PosterObjectKey = posterKey;
    }

    /// <summary>
    /// Mark asset as failed due to processing error.
    /// </summary>
    public void MarkFailed(string errorMessage)
    {
        Status = AssetStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
        MetadataJson["error"] = errorMessage;
    }

    /// <summary>
    /// Move the asset to Trash. Idempotent — soft-deleting an already-deleted asset is a no-op.
    /// The actual row stays; a background worker purges it after the configured TTL.
    /// </summary>
    public void MarkDeleted(string userId)
    {
        if (DeletedAt is not null) return;
        DeletedAt = DateTime.UtcNow;
        DeletedByUserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Restore a soft-deleted asset. Idempotent on already-restored rows.</summary>
    public void MarkRestored()
    {
        DeletedAt = null;
        DeletedByUserId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Validate asset type and content type compatibility.
    /// </summary>
    public bool IsValidContentType()
    {
        return (AssetType, ContentType) switch
        {
            (AssetType.Image, var ct) => ct.StartsWith("image/"),
            (AssetType.Video, var ct) => ct.StartsWith("video/"),
            (AssetType.Audio, var ct) => ct.StartsWith("audio/"),
            (AssetType.Document, var ct) => ct.StartsWith("application/pdf") || ct.StartsWith("application/vnd."),
            _ => false
        };
    }

}
