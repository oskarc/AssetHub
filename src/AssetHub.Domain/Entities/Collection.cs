namespace AssetHub.Domain.Entities;

public class Collection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;

    // Navigation
    public ICollection<CollectionAcl> Acls { get; set; } = new List<CollectionAcl>();
    
    /// <summary>
    /// Assets that are linked to this collection (via the many-to-many relationship).
    /// </summary>
    public ICollection<AssetCollection> AssetCollections { get; set; } = new List<AssetCollection>();
}
