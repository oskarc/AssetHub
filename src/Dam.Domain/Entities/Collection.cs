namespace Dam.Domain.Entities;

public class Collection
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;

    // Navigation
    public Collection? Parent { get; set; }
    public ICollection<Collection> Children { get; set; } = new List<Collection>();
    public ICollection<CollectionAcl> Acls { get; set; } = new List<CollectionAcl>();
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
}
