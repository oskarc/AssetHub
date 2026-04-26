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

    /// <summary>
    /// Optional parent collection for nested taxonomies (T5-NEST-01). Null = root.
    /// FK has <c>OnDelete.SetNull</c> so deleting a parent orphans children to
    /// root level rather than cascading the delete — the existing soft-delete /
    /// trash story is asset-only, and accidentally cascade-deleting a subtree
    /// of collections would be unrecoverable.
    /// </summary>
    public Guid? ParentCollectionId { get; set; }
    public Collection? Parent { get; set; }
    public ICollection<Collection> Children { get; set; } = new List<Collection>();

    /// <summary>
    /// When true, authorization checks walk this collection's parent chain
    /// looking for an ACL grant — the AEM "break inheritance" model from
    /// T5-NEST-01. Default false (flat ACL, identical to today's behaviour);
    /// the auth hot path only walks for collections that opted in. Walking
    /// stops at the first ancestor with this flag set to false.
    /// </summary>
    public bool InheritParentAcl { get; set; }

    // Navigation
    public ICollection<CollectionAcl> Acls { get; set; } = new List<CollectionAcl>();

    /// <summary>
    /// Assets that are linked to this collection (via the many-to-many relationship).
    /// </summary>
    public ICollection<AssetCollection> AssetCollections { get; set; } = new List<AssetCollection>();
}
