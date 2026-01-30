namespace Dam.Application.Dtos;

/// <summary>
/// DTO for updating asset metadata.
/// </summary>
public class UpdateAssetDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<string>? Tags { get; set; }
    public Dictionary<string, object>? MetadataJson { get; set; }
}
