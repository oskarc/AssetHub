using System.Text.Json.Serialization;

namespace Dam.Application.Dtos;

/// <summary>
/// DTO representing a collection an asset belongs to.
/// </summary>
public class AssetCollectionDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; }
}
