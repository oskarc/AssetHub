namespace AssetHub.Domain.Entities;

public class Taxonomy
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public ICollection<TaxonomyTerm> Terms { get; set; } = new List<TaxonomyTerm>();
}
