using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

// ── Response DTO ────────────────────────────────────────────────────────

public class AssetMetadataValueDto
{
    public required Guid MetadataFieldId { get; set; }
    public required string FieldKey { get; set; }
    public required string FieldLabel { get; set; }
    public required string FieldType { get; set; }
    public string? ValueText { get; set; }
    public decimal? ValueNumeric { get; set; }
    public DateTime? ValueDate { get; set; }
    public Guid? ValueTaxonomyTermId { get; set; }
    public string? ValueTaxonomyTermLabel { get; set; }
}

// ── Set value DTO ───────────────────────────────────────────────────────

public class SetAssetMetadataDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public required List<SetMetadataValueDto> Values { get; set; }
}

public class SetMetadataValueDto
{
    [Required]
    public required Guid MetadataFieldId { get; set; }

    public string? ValueText { get; set; }
    public decimal? ValueNumeric { get; set; }
    public DateTime? ValueDate { get; set; }
    public Guid? ValueTaxonomyTermId { get; set; }
}

// ── Bulk metadata DTO ───────────────────────────────────────────────────

public class BulkSetAssetMetadataDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public required List<BulkAssetMetadataEntry> Assets { get; set; }
}

public class BulkAssetMetadataEntry
{
    [Required]
    public required Guid AssetId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public required List<SetMetadataValueDto> Values { get; set; }
}
