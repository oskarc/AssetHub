using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

// ── Export Preset DTOs ──────────────────────────────────────────────────

/// <summary>
/// Response DTO for an export preset.
/// </summary>
public class ExportPresetDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public required string FitMode { get; set; }
    public required string Format { get; set; }
    public required int Quality { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required DateTime UpdatedAt { get; set; }
    public required string CreatedByUserId { get; set; }
}

/// <summary>
/// Request DTO for creating an export preset. Admin-only.
/// </summary>
public class CreateExportPresetDto
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Name { get; set; }

    [Range(1, 10000)]
    public int? Width { get; set; }

    [Range(1, 10000)]
    public int? Height { get; set; }

    [Required]
    [StringLength(50)]
    public required string FitMode { get; set; }

    [Required]
    [StringLength(50)]
    public required string Format { get; set; }

    [Range(1, 100)]
    public int Quality { get; set; } = 85;
}

/// <summary>
/// Request DTO for updating an export preset. Nullable properties = don't update.
/// </summary>
public class UpdateExportPresetDto
{
    [StringLength(255, MinimumLength = 1)]
    public string? Name { get; set; }

    [Range(1, 10000)]
    public int? Width { get; set; }

    [Range(1, 10000)]
    public int? Height { get; set; }

    [StringLength(50)]
    public string? FitMode { get; set; }

    [StringLength(50)]
    public string? Format { get; set; }

    [Range(1, 100)]
    public int? Quality { get; set; }
}
