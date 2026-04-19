namespace AssetHub.Domain.Entities;

public class MetadataSchema
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MetadataSchemaScope Scope { get; set; }
    public AssetType? AssetType { get; set; }
    public Guid? CollectionId { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public ICollection<MetadataField> Fields { get; set; } = new List<MetadataField>();
}
