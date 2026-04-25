namespace AssetHub.Domain.Entities;

public class Collection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;

    /// <summary>
    /// Optional <see cref="Brand"/> applied to public share pages whose
    /// scope resolves to this collection (T4-BP-01). Null = use the
    /// global default brand, or fall back to the unbranded theme.
    /// FK has <c>OnDelete.SetNull</c> so deleting a brand quietly demotes
    /// every collection that referenced it.
    /// </summary>
    public Guid? BrandId { get; set; }
    public Brand? Brand { get; set; }

    // Navigation
    public ICollection<CollectionAcl> Acls { get; set; } = new List<CollectionAcl>();

    /// <summary>
    /// Assets that are linked to this collection (via the many-to-many relationship).
    /// </summary>
    public ICollection<AssetCollection> AssetCollections { get; set; } = new List<AssetCollection>();
}
