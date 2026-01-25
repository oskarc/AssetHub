namespace Dam.Application.Dtos;

public class AssetResponseDto
{
    public required Guid Id { get; set; }
    public required Guid CollectionId { get; set; }
    public required string AssetType { get; set; }
    public required string Status { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> MetadataJson { get; set; } = new();
    public required string ContentType { get; set; }
    public required long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? ThumbObjectKey { get; set; }
    public string? MediumObjectKey { get; set; }
    public string? PosterObjectKey { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string CreatedByUserId { get; set; }
    public required DateTime UpdatedAt { get; set; }
}
