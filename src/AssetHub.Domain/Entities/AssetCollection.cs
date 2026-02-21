namespace AssetHub.Domain.Entities;

/// <summary>
/// Join entity for the many-to-many relationship between Assets and Collections.
/// An asset can belong to any number of collections; all memberships are equal.
/// </summary>
public class AssetCollection
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid CollectionId { get; set; }
    public DateTime AddedAt { get; set; }
    public string AddedByUserId { get; set; } = string.Empty;

    // Navigation properties
    public Asset Asset { get; set; } = null!;
    public Collection Collection { get; set; } = null!;
}
