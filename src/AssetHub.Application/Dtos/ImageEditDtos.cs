using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

// ── Image Edit DTOs ─────────────────────────────────────────────────────

/// <summary>
/// Save mode for an image edit operation.
/// </summary>
public enum ImageEditSaveMode
{
    /// <summary>Overwrite the source asset's file. Requires manager+ on the source collection.</summary>
    Replace,
    /// <summary>Create a new asset linked to the source via SourceAssetId.</summary>
    Copy,
    /// <summary>Create a copy and additionally generate derivative assets for each selected preset.</summary>
    CopyWithPresets
}

/// <summary>
/// Request DTO for saving an image edit. Submitted alongside the rendered PNG.
/// </summary>
public class ImageEditRequestDto
{
    [Required]
    public required ImageEditSaveMode SaveMode { get; set; }

    /// <summary>
    /// Export preset IDs to apply. Required when SaveMode is CopyWithPresets.
    /// </summary>
    public Guid[]? PresetIds { get; set; }

    /// <summary>
    /// Optional new title for the copy. Defaults to "&lt;original&gt; (edited)".
    /// </summary>
    [StringLength(255)]
    public string? Title { get; set; }

    /// <summary>
    /// The fabric.js edit document JSON for persisting and re-opening edits.
    /// Size-bounded to 256 KB.
    /// </summary>
    [StringLength(262144)]
    public string? EditDocument { get; set; }

    /// <summary>
    /// Optional destination collection for the copy. If null, copies to same collections as source.
    /// </summary>
    public Guid? DestinationCollectionId { get; set; }
}

/// <summary>
/// Response DTO for an image edit operation.
/// </summary>
public class ImageEditResultDto
{
    /// <summary>The ID of the resulting asset (the replaced original or the new copy).</summary>
    public required Guid AssetId { get; set; }

    /// <summary>IDs of derivative assets queued for preset generation (empty for Replace/Copy).</summary>
    public List<Guid> DerivativeAssetIds { get; set; } = new();
}

/// <summary>
/// Minimal projection of a derivative asset, used in the "Derivatives" panel on asset detail.
/// </summary>
public class AssetDerivativeDto
{
    public required Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; }
    public required string ContentType { get; set; }
    public required long SizeBytes { get; set; }
    public string? ThumbObjectKey { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}
