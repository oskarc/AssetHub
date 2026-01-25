namespace Dam.Domain.Entities;

public class CollectionAcl
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string PrincipalType { get; set; } = string.Empty; // "user" or "group"
    public string PrincipalId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // viewer|contributor|manager|admin
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Collection Collection { get; set; } = null!;
}

public enum CollectionRole
{
    Viewer,
    Contributor,
    Manager,
    Admin
}
