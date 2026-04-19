namespace AssetHub.Domain.Entities;

public class AssetMetadataValue
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid MetadataFieldId { get; set; }
    public string? ValueText { get; set; }
    public decimal? ValueNumeric { get; set; }
    public DateTime? ValueDate { get; set; }
    public Guid? ValueTaxonomyTermId { get; set; }
    public Asset? Asset { get; set; }
    public MetadataField? MetadataField { get; set; }
    public TaxonomyTerm? ValueTaxonomyTerm { get; set; }
}
