using System.Text.Json.Serialization;
using Dam.Application.Dtos;

namespace Dam.Ui.Services;

public class AllAssetsListResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<AssetResponseDto> Items { get; set; } = new();
}
