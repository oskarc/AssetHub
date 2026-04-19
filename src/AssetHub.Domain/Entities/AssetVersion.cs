namespace AssetHub.Domain.Entities;

/// <summary>
/// A historical snapshot of an Asset taken when its bytes change (e.g., the
/// image-editor Replace flow). The current state always lives on the Asset row;
/// versions store everything needed to roll back: object keys, hash, edit document,
/// and a metadata snapshot. Restoring a version overwrites the Asset row from the
/// snapshot and bumps CurrentVersionNumber.
/// </summary>
public class AssetVersion
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }

    /// <summary>1-based, unique per AssetId. v1 is captured the first time an asset is replaced.</summary>
    public int VersionNumber { get; set; }

    public string OriginalObjectKey { get; set; } = string.Empty;
    public string? ThumbObjectKey { get; set; }
    public string? MediumObjectKey { get; set; }
    public string? PosterObjectKey { get; set; }

    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Snapshot of the Asset's EditDocument at the time the version was taken.</summary>
    public string? EditDocument { get; set; }

    /// <summary>
    /// Snapshot of Asset.MetadataJson when the version was created. Stored as JSONB so
    /// future diff-views can compare snapshots without joining audit history.
    /// </summary>
    public Dictionary<string, object> MetadataSnapshot { get; set; } = new();

    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Optional free-text reason supplied by the user when replacing the file.</summary>
    public string? ChangeNote { get; set; }
}
