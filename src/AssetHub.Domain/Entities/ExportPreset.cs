namespace AssetHub.Domain.Entities;

/// <summary>
/// Admin-managed export preset that defines a target size, format, and quality
/// for derivative images produced by the image editor.
/// </summary>
public class ExportPreset
{
    public Guid Id { get; set; }

    /// <summary>Display name, e.g. "Square 1080", "Story 9:16", "Email thumb".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Target width in pixels. Null when FitMode is Height.</summary>
    public int? Width { get; set; }

    /// <summary>Target height in pixels. Null when FitMode is Width.</summary>
    public int? Height { get; set; }

    public ExportPresetFitMode FitMode { get; set; } = ExportPresetFitMode.Contain;

    public ExportPresetFormat Format { get; set; } = ExportPresetFormat.Original;

    /// <summary>Output quality 1–100. Applies to JPEG and WebP.</summary>
    public int Quality { get; set; } = 85;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
}
