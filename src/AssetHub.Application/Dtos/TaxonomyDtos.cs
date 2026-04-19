using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

// ── Response DTOs ───────────────────────────────────────────────────────

public class TaxonomyDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string CreatedByUserId { get; set; }
    public required List<TaxonomyTermDto> Terms { get; set; }
}

public class TaxonomyTermDto
{
    public required Guid Id { get; set; }
    public Guid? ParentTermId { get; set; }
    public required string Label { get; set; }
    public string? LabelSv { get; set; }
    public required string Slug { get; set; }
    public required int SortOrder { get; set; }
    public List<TaxonomyTermDto>? Children { get; set; }
}

public class TaxonomySummaryDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required int TermCount { get; set; }
}

// ── Create DTOs ─────────────────────────────────────────────────────────

public class CreateTaxonomyDto
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Name { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public List<CreateTaxonomyTermDto>? Terms { get; set; }
}

public class CreateTaxonomyTermDto
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Label { get; set; }

    [StringLength(255)]
    public string? LabelSv { get; set; }

    [StringLength(255)]
    public string? Slug { get; set; }

    public Guid? ParentTermId { get; set; }

    [Range(0, 10000)]
    public int SortOrder { get; set; }

    [MaxLength(500)]
    public List<CreateTaxonomyTermDto>? Children { get; set; }
}

// ── Update DTOs ─────────────────────────────────────────────────────────

public class UpdateTaxonomyDto
{
    [StringLength(255, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }
}

public class UpsertTaxonomyTermDto
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Label { get; set; }

    [StringLength(255)]
    public string? LabelSv { get; set; }

    [StringLength(255)]
    public string? Slug { get; set; }

    public Guid? ParentTermId { get; set; }

    [Range(0, 10000)]
    public int SortOrder { get; set; }

    [MaxLength(500)]
    public List<UpsertTaxonomyTermDto>? Children { get; set; }
}
