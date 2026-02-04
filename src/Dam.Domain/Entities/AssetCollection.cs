namespace Dam.Domain.Entities;

/// <summary>
/// Join entity for many-to-many relationship between Assets and Collections.
/// This allows an asset to belong to multiple collections beyond its primary collection.
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
