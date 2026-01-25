namespace Dam.Application.Dtos;

public class CreateAssetDto
{
    public required Guid CollectionId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object>? MetadataJson { get; set; }
}
