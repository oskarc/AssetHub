namespace Dam.Domain.Entities;

/// <summary>
/// Access Control List entry for a collection.
/// Links a principal (user) to a collection with a specific role.
/// </summary>
public class CollectionAcl
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public PrincipalType PrincipalType { get; set; } = PrincipalType.User;
    public string PrincipalId { get; set; } = string.Empty;
    public AclRole Role { get; set; } = AclRole.Viewer;
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Collection Collection { get; set; } = null!;
}
