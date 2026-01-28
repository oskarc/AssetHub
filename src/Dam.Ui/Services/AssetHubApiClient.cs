using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Dam.Application.Dtos;

namespace Dam.Ui.Services;

/// <summary>
/// HTTP client for AssetHub API endpoints.
/// </summary>
public class AssetHubApiClient
{
    private readonly HttpClient _http;

    public AssetHubApiClient(HttpClient http)
    {
        _http = http;
    }

    #region Collections

    public async Task<List<CollectionResponseDto>> GetCollectionsAsync(Guid? parentId = null)
    {
        var url = parentId.HasValue
            ? $"/api/collections/{parentId}/children"
            : "/api/collections";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CollectionResponseDto>>() ?? new();
    }

    public async Task<CollectionResponseDto?> GetCollectionAsync(Guid id)
    {
        return await _http.GetFromJsonAsync<CollectionResponseDto>($"/api/collections/{id}");
    }

    public async Task<CollectionResponseDto> CreateCollectionAsync(CreateCollectionDto dto)
    {
        var response = await _http.PostAsJsonAsync("/api/collections", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CollectionResponseDto>()
            ?? throw new InvalidOperationException("Failed to create collection");
    }

    public async Task<CollectionResponseDto> CreateSubCollectionAsync(Guid parentId, CreateCollectionDto dto)
    {
        var response = await _http.PostAsJsonAsync($"/api/collections/{parentId}/children", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CollectionResponseDto>()
            ?? throw new InvalidOperationException("Failed to create sub-collection");
    }

    public async Task UpdateCollectionAsync(Guid id, UpdateCollectionDto dto)
    {
        var response = await _http.PatchAsJsonAsync($"/api/collections/{id}", dto);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteCollectionAsync(Guid id)
    {
        var response = await _http.DeleteAsync($"/api/collections/{id}");
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region Assets

    public async Task<AssetListResponse> GetAssetsAsync(
        Guid collectionId,
        string? query = null,
        string? type = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50)
    {
        var url = $"/api/assets/collection/{collectionId}?skip={skip}&take={take}&sortBy={sortBy}";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(type))
            url += $"&type={Uri.EscapeDataString(type)}";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssetListResponse>()
            ?? new AssetListResponse { CollectionId = collectionId, Total = 0, Items = new() };
    }

    public async Task<AssetResponseDto?> GetAssetAsync(Guid id)
    {
        return await _http.GetFromJsonAsync<AssetResponseDto>($"/api/assets/{id}");
    }

    public async Task<AssetUploadResult> UploadAssetAsync(Guid collectionId, string title, Stream fileStream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        content.Add(new StringContent(collectionId.ToString()), "collectionId");
        content.Add(new StringContent(title), "title");

        var response = await _http.PostAsync("/api/assets", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssetUploadResult>()
            ?? throw new InvalidOperationException("Failed to upload asset");
    }

    public async Task DeleteAssetAsync(Guid id)
    {
        var response = await _http.DeleteAsync($"/api/assets/{id}");
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region Shares

    public async Task<ShareResponse> CreateShareAsync(Guid scopeId, string scopeType, DateTime? expiresAt = null, string? password = null)
    {
        var dto = new
        {
            ScopeId = scopeId,
            ScopeType = scopeType,
            ExpiresAt = expiresAt ?? DateTime.Now.AddDays(7),
            Password = password
        };

        var response = await _http.PostAsJsonAsync("/api/shares", dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ShareResponse>()
            ?? throw new InvalidOperationException("Failed to create share");
    }

    public async Task RevokeShareAsync(Guid id)
    {
        var response = await _http.DeleteAsync($"/api/shares/{id}");
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> GetSharedContentAsync(string token, string? password = null)
    {
        var url = $"/api/shares/{Uri.EscapeDataString(token)}";
        if (!string.IsNullOrEmpty(password))
            url += $"?password={Uri.EscapeDataString(password)}";
        return await _http.GetAsync(url);
    }

    #endregion

    #region Presigned URLs

    public async Task<string> GetPresignedDownloadUrlAsync(Guid assetId, string objectKey)
    {
        // For now, construct the URL to download through MinIO presigned
        // In production, this would be a dedicated endpoint
        var response = await _http.GetAsync($"/api/assets/{assetId}");
        if (response.IsSuccessStatusCode)
        {
            // The asset endpoint should return presigned URLs in a real implementation
            // For now, we'll use the object key directly through the MinIO endpoint
            return $"/api/assets/{assetId}/download";
        }
        return string.Empty;
    }

    #endregion
}

#region Response Types

public class AssetListResponse
{
    [JsonPropertyName("collectionId")]
    public Guid CollectionId { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<AssetResponseDto> Items { get; set; } = new();
}

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

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("shareUrl")]
    public string ShareUrl { get; set; } = "";
}

#endregion
