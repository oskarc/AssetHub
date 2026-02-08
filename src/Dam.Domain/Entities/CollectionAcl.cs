namespace Dam.Domain.Entities;

/// <summary>
/// Access Control List entry for a collection.
/// Links a principal (user) to a collection with a specific role.
/// </summary>
public class CollectionAcl
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string PrincipalType { get; set; } = string.Empty; // "user"
    public string PrincipalId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // viewer|contributor|manager|admin
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Collection Collection { get; set; } = null!;
}
