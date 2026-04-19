using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

// ── Response DTOs ───────────────────────────────────────────────────────

public class MetadataSchemaDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Scope { get; set; }
    public string? AssetType { get; set; }
    public Guid? CollectionId { get; set; }
    public required int Version { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string CreatedByUserId { get; set; }
    public required List<MetadataFieldDto> Fields { get; set; }
}

public class MetadataFieldDto
{
    public required Guid Id { get; set; }
    public required string Key { get; set; }
    public required string Label { get; set; }
    public string? LabelSv { get; set; }
    public required string Type { get; set; }
    public required bool Required { get; set; }
    public required bool Searchable { get; set; }
    public required bool Facetable { get; set; }
    public string? PatternRegex { get; set; }
    public int? MaxLength { get; set; }
    public decimal? NumericMin { get; set; }
    public decimal? NumericMax { get; set; }
    public List<string>? SelectOptions { get; set; }
    public Guid? TaxonomyId { get; set; }
    public required int SortOrder { get; set; }
}

// ── Create DTOs ─────────────────────────────────────────────────────────

public class CreateMetadataSchemaDto
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Name { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required]
    [StringLength(50)]
    public required string Scope { get; set; }

    [StringLength(50)]
    public string? AssetType { get; set; }

    public Guid? CollectionId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public required List<CreateMetadataFieldDto> Fields { get; set; }
}

public class CreateMetadataFieldDto
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    [RegularExpression(@"^[a-z][a-z0-9_]*$", ErrorMessage = "Key must be snake_case (lowercase letters, digits, underscores, starting with a letter).")]
    public required string Key { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Label { get; set; }

    [StringLength(255)]
    public string? LabelSv { get; set; }

    [Required]
    [StringLength(50)]
    public required string Type { get; set; }

    public bool Required { get; set; }
    public bool Searchable { get; set; } = true;
    public bool Facetable { get; set; }

    [StringLength(500)]
    public string? PatternRegex { get; set; }

    [Range(1, 10000)]
    public int? MaxLength { get; set; }

    public decimal? NumericMin { get; set; }
    public decimal? NumericMax { get; set; }

    [MaxLength(100)]
    public List<string>? SelectOptions { get; set; }

    public Guid? TaxonomyId { get; set; }

    [Range(0, 1000)]
    public int SortOrder { get; set; }
}

// ── Update DTOs ─────────────────────────────────────────────────────────

public class UpdateMetadataSchemaDto
{
    [StringLength(255, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public List<UpdateMetadataFieldDto>? Fields { get; set; }
}

public class UpdateMetadataFieldDto
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    [RegularExpression(@"^[a-z][a-z0-9_]*$", ErrorMessage = "Key must be snake_case.")]
    public required string Key { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Label { get; set; }

    [StringLength(255)]
    public string? LabelSv { get; set; }

    [Required]
    [StringLength(50)]
    public required string Type { get; set; }

    public bool Required { get; set; }
    public bool Searchable { get; set; } = true;
    public bool Facetable { get; set; }

    [StringLength(500)]
    public string? PatternRegex { get; set; }

    [Range(1, 10000)]
    public int? MaxLength { get; set; }

    public decimal? NumericMin { get; set; }
    public decimal? NumericMax { get; set; }

    [MaxLength(100)]
    public List<string>? SelectOptions { get; set; }

    public Guid? TaxonomyId { get; set; }

    [Range(0, 1000)]
    public int SortOrder { get; set; }
}
