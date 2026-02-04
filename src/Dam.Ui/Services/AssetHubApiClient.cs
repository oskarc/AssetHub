using System.Net.Http.Json;
using Dam.Application.Dtos;

namespace Dam.Ui.Services;

/// <summary>
/// HTTP client for AssetHub API endpoints.
/// Provides a strongly-typed interface for all API operations with consistent error handling.
/// </summary>
/// <remarks>
/// All methods use <see cref="EnsureSuccessAsync"/> for consistent error handling.
/// On failure, an <see cref="ApiException"/> is thrown with the server's error message.
/// </remarks>
public class AssetHubApiClient
{
    private readonly HttpClient _http;

    public AssetHubApiClient(HttpClient http)
    {
        _http = http;
    }

    #region Helpers

    /// <summary>
    /// Ensures the response is successful, throwing an exception with the server's error message if not.
    /// </summary>
    /// <param name="response">The HTTP response to check.</param>
    /// <param name="operation">A human-readable description of the operation (used in error messages).</param>
    /// <exception cref="ApiException">Thrown when the response indicates failure.</exception>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            var message = ExtractErrorMessage(errorContent, operation, response);
            throw new ApiException(message, response.StatusCode);
        }
    }
    
    /// <summary>
    /// Extracts a user-friendly error message from the response content.
    /// Handles JSON error responses like {"error": "message"}.
    /// </summary>
    private static string ExtractErrorMessage(string errorContent, string operation, HttpResponseMessage response)
    {
        if (string.IsNullOrWhiteSpace(errorContent))
            return $"{operation} failed with status {(int)response.StatusCode} ({response.ReasonPhrase})";
        
        // Try to parse as JSON error object
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(errorContent);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                return errorProp.GetString() ?? errorContent;
            }
            if (doc.RootElement.TryGetProperty("message", out var messageProp))
            {
                return messageProp.GetString() ?? errorContent;
            }
        }
        catch
        {
            // Not JSON, return as-is
        }
        
        return errorContent;
    }

    /// <summary>
    /// Reads the response content as JSON, throwing if the result is null.
    /// </summary>
    private static async Task<T> ReadRequiredJsonAsync<T>(HttpResponseMessage response, string operation)
    {
        var result = await response.Content.ReadFromJsonAsync<T>();
        return result ?? throw new ApiException($"{operation} returned an empty response", response.StatusCode);
    }

    #endregion

    #region Collections

    /// <summary>
    /// Gets all collections, optionally filtered by parent ID.
    /// </summary>
    /// <param name="parentId">If specified, returns only children of this collection.</param>
    /// <returns>List of collections (empty if none found).</returns>
    public async Task<List<CollectionResponseDto>> GetCollectionsAsync(Guid? parentId = null)
    {
        var url = parentId.HasValue
            ? $"/api/collections/{parentId}/children"
            : "/api/collections";

        var response = await _http.GetAsync(url);
        await EnsureSuccessAsync(response, "Get collections");
        return await response.Content.ReadFromJsonAsync<List<CollectionResponseDto>>() ?? new();
    }

    /// <summary>
    /// Gets a single collection by ID.
    /// </summary>
    /// <returns>The collection, or null if not found.</returns>
    public async Task<CollectionResponseDto?> GetCollectionAsync(Guid id)
    {
        var response = await _http.GetAsync($"/api/collections/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, "Get collection");
        return await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
    }

    /// <summary>
    /// Creates a new root-level collection.
    /// </summary>
    public async Task<CollectionResponseDto> CreateCollectionAsync(CreateCollectionDto dto)
    {
        var response = await _http.PostAsJsonAsync("/api/collections", dto);
        await EnsureSuccessAsync(response, "Create collection");
        return await ReadRequiredJsonAsync<CollectionResponseDto>(response, "Create collection");
    }

    /// <summary>
    /// Creates a new sub-collection under a parent.
    /// </summary>
    public async Task<CollectionResponseDto> CreateSubCollectionAsync(Guid parentId, CreateCollectionDto dto)
    {
        var response = await _http.PostAsJsonAsync($"/api/collections/{parentId}/children", dto);
        await EnsureSuccessAsync(response, "Create sub-collection");
        return await ReadRequiredJsonAsync<CollectionResponseDto>(response, "Create sub-collection");
    }

    /// <summary>
    /// Updates an existing collection.
    /// </summary>
    public async Task UpdateCollectionAsync(Guid id, UpdateCollectionDto dto)
    {
        var response = await _http.PatchAsJsonAsync($"/api/collections/{id}", dto);
        await EnsureSuccessAsync(response, "Update collection");
    }

    /// <summary>
    /// Deletes a collection.
    /// </summary>
    public async Task DeleteCollectionAsync(Guid id)
    {
        var response = await _http.DeleteAsync($"/api/collections/{id}");
        await EnsureSuccessAsync(response, "Delete collection");
    }

    #endregion

    #region Assets

    /// <summary>
    /// Gets assets in a specific collection with optional filtering.
    /// </summary>
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
        await EnsureSuccessAsync(response, "Get assets");
        return await response.Content.ReadFromJsonAsync<AssetListResponse>()
            ?? new AssetListResponse { CollectionId = collectionId, Total = 0, Items = new() };
    }

    /// <summary>
    /// Gets all assets across all collections with optional filtering.
    /// </summary>
    public async Task<AllAssetsListResponse> GetAllAssetsAsync(
        string? query = null,
        string? type = null,
        Guid? collectionId = null,
        string sortBy = "created_desc",
        int skip = 0,
        int take = 50)
    {
        var url = $"/api/assets/all?skip={skip}&take={take}&sortBy={sortBy}";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(type))
            url += $"&type={Uri.EscapeDataString(type)}";
        if (collectionId.HasValue)
            url += $"&collectionId={collectionId.Value}";

        var response = await _http.GetAsync(url);
        await EnsureSuccessAsync(response, "Get all assets");
        return await response.Content.ReadFromJsonAsync<AllAssetsListResponse>()
            ?? new AllAssetsListResponse { Total = 0, Items = new() };
    }

    /// <summary>
    /// Gets a single asset by ID.
    /// </summary>
    /// <returns>The asset, or null if not found.</returns>
    public async Task<AssetResponseDto?> GetAssetAsync(Guid id)
    {
        var response = await _http.GetAsync($"/api/assets/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, "Get asset");
        return await response.Content.ReadFromJsonAsync<AssetResponseDto>();
    }

    /// <summary>
    /// Updates an existing asset's metadata.
    /// </summary>
    public async Task<AssetResponseDto> UpdateAssetAsync(Guid id, UpdateAssetDto dto)
    {
        var response = await _http.PatchAsJsonAsync($"/api/assets/{id}", dto);
        await EnsureSuccessAsync(response, "Update asset");
        return await ReadRequiredJsonAsync<AssetResponseDto>(response, "Update asset");
    }

    /// <summary>
    /// Uploads a new asset to a collection.
    /// </summary>
    public async Task<AssetUploadResult> UploadAssetAsync(
        Guid collectionId,
        string title,
        Stream fileStream,
        string fileName,
        string contentType)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        content.Add(new StringContent(collectionId.ToString()), "collectionId");
        content.Add(new StringContent(title), "title");

        var response = await _http.PostAsync("/api/assets", content);
        await EnsureSuccessAsync(response, "Upload asset");
        return await ReadRequiredJsonAsync<AssetUploadResult>(response, "Upload asset");
    }

    /// <summary>
    /// Deletes an asset.
    /// </summary>
    public async Task DeleteAssetAsync(Guid id)
    {
        var response = await _http.DeleteAsync($"/api/assets/{id}");
        await EnsureSuccessAsync(response, "Delete asset");
    }

    #endregion

    #region Shares

    /// <summary>
    /// Creates a new share link for an asset or collection.
    /// </summary>
    /// <param name="scopeId">The ID of the asset or collection to share.</param>
    /// <param name="scopeType">"asset" or "collection".</param>
    /// <param name="expiresAt">When the share expires (defaults to 7 days).</param>
    /// <param name="password">Optional password (if not provided, one will be generated).</param>
    /// <param name="notifyEmails">Optional list of email addresses to notify about the share.</param>
    public async Task<ShareResponse> CreateShareAsync(
        Guid scopeId,
        string scopeType,
        DateTime? expiresAt = null,
        string? password = null,
        List<string>? notifyEmails = null)
    {
        var dto = new
        {
            ScopeId = scopeId,
            ScopeType = scopeType,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            Password = password,
            NotifyEmails = notifyEmails
        };

        var response = await _http.PostAsJsonAsync("/api/shares", dto);
        await EnsureSuccessAsync(response, "Create share");
        return await ReadRequiredJsonAsync<ShareResponse>(response, "Create share");
    }

    /// <summary>
    /// Updates the password for an existing share.
    /// </summary>
    public async Task UpdateSharePasswordAsync(Guid shareId, string newPassword)
    {
        var response = await _http.PutAsJsonAsync($"/api/shares/{shareId}/password", new { Password = newPassword });
        await EnsureSuccessAsync(response, "Update share password");
    }

    /// <summary>
    /// Revokes a share link (user-level, for own shares).
    /// </summary>
    public async Task RevokeShareAsync(Guid id)
    {
        var response = await _http.DeleteAsync($"/api/shares/{id}");
        await EnsureSuccessAsync(response, "Revoke share");
    }

    /// <summary>
    /// Gets shared content by token (for public share pages).
    /// </summary>
    /// <remarks>
    /// Returns the raw response to allow handling password prompts and different content types.
    /// </remarks>
    public async Task<HttpResponseMessage> GetSharedContentAsync(string token, string? password = null)
    {
        var url = $"/api/shares/{Uri.EscapeDataString(token)}";
        if (!string.IsNullOrEmpty(password))
            url += $"?password={Uri.EscapeDataString(password)}";
        return await _http.GetAsync(url);
    }

    #endregion

    #region Presigned URLs

    /// <summary>
    /// Gets a presigned download URL for an asset.
    /// </summary>
    public async Task<string> GetPresignedDownloadUrlAsync(Guid assetId, string objectKey)
    {
        var response = await _http.GetAsync($"/api/assets/{assetId}");
        if (response.IsSuccessStatusCode)
        {
            // The asset endpoint returns presigned URLs in the response
            // For now, we'll use the direct download endpoint
            return $"/api/assets/{assetId}/download";
        }
        return string.Empty;
    }

    #endregion

    #region Admin

    /// <summary>
    /// Gets all shares in the system (admin only).
    /// </summary>
    public async Task<List<AdminShareDto>> GetAllSharesAsync()
    {
        var response = await _http.GetAsync("/api/admin/shares");
        await EnsureSuccessAsync(response, "Get all shares");
        return await response.Content.ReadFromJsonAsync<List<AdminShareDto>>() ?? new();
    }

    /// <summary>
    /// Revokes any share by ID (admin only).
    /// </summary>
    public async Task RevokeShareAdminAsync(Guid id)
    {
        var response = await _http.PostAsync($"/api/admin/shares/{id}/revoke", null);
        await EnsureSuccessAsync(response, "Revoke share");
    }

    /// <summary>
    /// Gets all collections with their ACLs (admin only).
    /// </summary>
    public async Task<List<CollectionAccessDto>> GetCollectionAccessAsync()
    {
        var response = await _http.GetAsync("/api/admin/collections/access");
        await EnsureSuccessAsync(response, "Get collection access");
        return await response.Content.ReadFromJsonAsync<List<CollectionAccessDto>>() ?? new();
    }

    /// <summary>
    /// Adds a user or group to a collection's ACL (admin only).
    /// </summary>
    public async Task AddCollectionAclAsync(
        Guid collectionId,
        string principalType,
        string principalId,
        string role)
    {
        var request = new { principalType, principalId, role };
        var response = await _http.PostAsJsonAsync($"/api/admin/collections/{collectionId}/acl", request);
        await EnsureSuccessAsync(response, "Add collection access");
    }

    /// <summary>
    /// Removes a user or group from a collection's ACL (admin only).
    /// </summary>
    public async Task RemoveCollectionAclAsync(Guid collectionId, string principalId, string principalType)
    {
        var url = $"/api/admin/collections/{collectionId}/acl/{Uri.EscapeDataString(principalId)}?principalType={principalType}";
        var response = await _http.DeleteAsync(url);
        await EnsureSuccessAsync(response, "Remove collection access");
    }

    /// <summary>
    /// Gets all users who have access to collections (admin only).
    /// </summary>
    public async Task<List<UserAccessSummaryDto>> GetUsersAsync()
    {
        var response = await _http.GetAsync("/api/admin/users");
        await EnsureSuccessAsync(response, "Get users");
        return await response.Content.ReadFromJsonAsync<List<UserAccessSummaryDto>>() ?? new();
    }

    #endregion
}

/// <summary>
/// Exception thrown when an API call fails.
/// Contains the HTTP status code and server error message for debugging.
/// </summary>
public class ApiException : Exception
{
    /// <summary>
    /// The HTTP status code returned by the server.
    /// </summary>
    public System.Net.HttpStatusCode StatusCode { get; }

    public ApiException(string message, System.Net.HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public override string ToString()
    {
        return $"ApiException: {Message} (HTTP {(int)StatusCode})";
    }
}
