using System.Text.Json.Serialization;

namespace Dam.Ui.Services;

public class AssetUploadResult
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("jobId")]
    public string? JobId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
