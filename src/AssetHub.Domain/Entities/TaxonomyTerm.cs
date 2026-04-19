namespace AssetHub.Domain.Entities;

public class TaxonomyTerm
{
    public Guid Id { get; set; }
    public Guid TaxonomyId { get; set; }
    public Guid? ParentTermId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? LabelSv { get; set; }
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public Taxonomy? Taxonomy { get; set; }
    public TaxonomyTerm? ParentTerm { get; set; }
    public ICollection<TaxonomyTerm> Children { get; set; } = new List<TaxonomyTerm>();
}
