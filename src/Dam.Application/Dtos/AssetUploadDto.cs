namespace Dam.Application.Dtos;

public class AssetUploadDto
{
    public required Guid CollectionId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object>? MetadataJson { get; set; }
    // File data will be handled separately via multipart form
}
