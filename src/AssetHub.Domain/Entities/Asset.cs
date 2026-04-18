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

    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

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
        if (thumbKey != null) ThumbObjectKey = thumbKey;
        if (mediumKey != null) MediumObjectKey = mediumKey;
        if (posterKey != null) PosterObjectKey = posterKey;
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
    /// Validate asset type and content type compatibility.
    /// </summary>
    public bool IsValidContentType()
    {
        return (AssetType, ContentType) switch
        {
            (AssetType.Image, var ct) => ct.StartsWith("image/"),
            (AssetType.Video, var ct) => ct.StartsWith("video/"),
            (AssetType.Document, var ct) => ct.StartsWith("application/pdf") || ct.StartsWith("application/vnd."),
            _ => false
        };
    }

}
