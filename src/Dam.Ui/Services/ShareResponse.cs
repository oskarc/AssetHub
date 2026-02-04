using System.Text.Json.Serialization;

namespace Dam.Ui.Services;

public class ShareResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("scopeType")]
    public string ScopeType { get; set; } = "";

    [JsonPropertyName("scopeId")]
    public Guid ScopeId { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("shareUrl")]
    public string ShareUrl { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}
