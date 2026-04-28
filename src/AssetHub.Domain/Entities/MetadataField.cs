using System.Text.Json.Serialization;

namespace AssetHub.Domain.Entities;

public class MetadataField
{
    public Guid Id { get; set; }
    public Guid MetadataSchemaId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? LabelSv { get; set; }
    public MetadataFieldType Type { get; set; }
    public bool Required { get; set; }
    public bool Searchable { get; set; } = true;
    public bool Facetable { get; set; }
    public string? PatternRegex { get; set; }
    public int? MaxLength { get; set; }
    public decimal? NumericMin { get; set; }
    public decimal? NumericMax { get; set; }
    public List<string> SelectOptions { get; set; } = new();
    public Guid? TaxonomyId { get; set; }
    public int SortOrder { get; set; }

    // Back-references — JsonIgnore breaks the MetadataSchema → Fields ↔ Field
    // cycle that crashed HybridCache JSON serialization. ID columns above
    // already carry the relationship for any consumer that needs it.
    [JsonIgnore]
    public MetadataSchema? MetadataSchema { get; set; }
    [JsonIgnore]
    public Taxonomy? Taxonomy { get; set; }
}
