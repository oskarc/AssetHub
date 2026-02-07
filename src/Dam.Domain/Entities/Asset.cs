namespace Dam.Domain.Entities;

public class Asset
{
    // Asset status constants
    public const string StatusUploading = "uploading";
    public const string StatusProcessing = "processing";
    public const string StatusReady = "ready";
    public const string StatusFailed = "failed";

    // Asset type constants
    public const string TypeImage = "image";
    public const string TypeVideo = "video";
    public const string TypeDocument = "document";

    public Guid Id { get; set; }
    public string AssetType { get; set; } = string.Empty; // image|video|document
    public string Status { get; set; } = StatusProcessing; // processing|ready|failed
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
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
    /// All collections this asset belongs to. All collections are equal - no hierarchy.
    /// </summary>
    public ICollection<AssetCollection> AssetCollections { get; set; } = new List<AssetCollection>();

    /// <summary>
    /// Mark asset as ready after processing completes.
    /// </summary>
    public void MarkReady(string? thumbKey = null, string? mediumKey = null, string? posterKey = null)
    {
        Status = StatusReady;
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
        Status = StatusFailed;
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
            (TypeImage, var ct) => ct.StartsWith("image/"),
            (TypeVideo, var ct) => ct.StartsWith("video/"),
            (TypeDocument, var ct) => ct.StartsWith("application/pdf") || ct.StartsWith("application/vnd."),
            _ => false
        };
    }

    /// <summary>
    /// Get human-readable file size string (e.g., "2.5 MB").
    /// </summary>
    public string GetHumanReadableSize()
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;

        return SizeBytes switch
        {
            < kb => $"{SizeBytes} B",
            < mb => $"{SizeBytes / (double)kb:F2} KB",
            < gb => $"{SizeBytes / (double)mb:F2} MB",
            _ => $"{SizeBytes / (double)gb:F2} GB"
        };
    }
}
