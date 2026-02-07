using System.Text.Json.Serialization;

namespace Dam.Ui.Services;

/// <summary>
/// Response from the init-upload API endpoint.
/// Contains the presigned PUT URL for direct-to-MinIO upload.
/// </summary>
public class InitUploadResult
{
    [JsonPropertyName("assetId")]
    public Guid AssetId { get; set; }

    [JsonPropertyName("objectKey")]
    public string ObjectKey { get; set; } = "";

    [JsonPropertyName("uploadUrl")]
    public string UploadUrl { get; set; } = "";

    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; set; }
}
