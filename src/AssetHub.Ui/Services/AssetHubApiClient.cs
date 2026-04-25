using System.Net.Http.Json;
using System.Text.Json;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Application;

namespace AssetHub.Ui.Services;

/// <summary>
/// HTTP client for AssetHub API endpoints.
/// Provides a strongly-typed interface for all API operations with consistent error handling.
/// </summary>
/// <remarks>
/// All methods use <see cref="EnsureSuccessAsync"/> for consistent error handling.
/// On failure, an <see cref="ApiException"/> is thrown with the server's error message.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Single HTTP-client facade for the UI — every domain DTO it serialises counts as a coupled type. Splitting into per-domain clients adds N facades for the same surface area.")]
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
            var (message, errorCode, details) = ExtractErrorInfo(errorContent, operation, response);
            throw new ApiException(message, response.StatusCode, errorCode, details);
        }
    }
    
    /// <summary>
    /// Extracts a user-friendly error message, error code, and details from the response content.
    /// Handles JSON error responses like {"code": "...", "message": "...", "details": {...}}.
    /// </summary>
    private static (string Message, string? Code, Dictionary<string, string>? Details) ExtractErrorInfo(
        string errorContent, string operation, HttpResponseMessage response)
    {
        if (string.IsNullOrWhiteSpace(errorContent))
            return ($"{operation} failed with status {(int)response.StatusCode} ({response.ReasonPhrase})", null, null);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(errorContent);
            var root = doc.RootElement;

            var code = TryGetStringProperty(root, "code");
            var details = ExtractDetailsDictionary(root);
            var message = ExtractFirstAvailableMessage(root);

            return (message ?? errorContent, code, details);
        }
        catch
        {
            // Not JSON, return as-is
        }

        return (errorContent, null, null);
    }

    private static string? TryGetStringProperty(System.Text.Json.JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) ? prop.GetString() : null;

    private static Dictionary<string, string>? ExtractDetailsDictionary(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("details", out var detailsProp)
            || detailsProp.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        var details = new Dictionary<string, string>();
        foreach (var prop in detailsProp.EnumerateObject())
        {
            details[prop.Name] = prop.Value.GetString() ?? prop.Value.ToString();
        }
        return details;
    }

    private static readonly string[] MessagePropertyNames = { "error", "message", "detail", "title" };

    private static string? ExtractFirstAvailableMessage(System.Text.Json.JsonElement root)
    {
        foreach (var name in MessagePropertyNames)
        {
            var value = TryGetStringProperty(root, name);
            if (value is not null) return value;
        }
        return null;
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

    #region Dashboard

    /// <summary>
    /// Gets aggregated dashboard data scoped to the current user's role.
    /// </summary>
    public virtual async Task<DashboardDto?> GetDashboardAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/dashboard", ct);
        await EnsureSuccessAsync(response, "Get dashboard");
        return await response.Content.ReadFromJsonAsync<DashboardDto>(ct);
    }

    #endregion

    #region Collections

    /// <summary>
    /// Gets all collections.
    /// </summary>
    /// <returns>List of collections (empty if none found).</returns>
    public virtual async Task<List<CollectionResponseDto>> GetCollectionsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/collections", ct);
        await EnsureSuccessAsync(response, "Get collections");
        return await response.Content.ReadFromJsonAsync<List<CollectionResponseDto>>(ct) ?? new();
    }

    /// <summary>
    /// Gets a single collection by ID.
    /// </summary>
    /// <returns>The collection, or null if not found.</returns>
    public virtual async Task<CollectionResponseDto?> GetCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/collections/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, "Get collection");
        return await response.Content.ReadFromJsonAsync<CollectionResponseDto>(ct);
    }

    /// <summary>
    /// Creates a new collection.
    /// </summary>
    public virtual async Task<CollectionResponseDto> CreateCollectionAsync(CreateCollectionDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/collections", dto, ct);
        await EnsureSuccessAsync(response, "Create collection");
        return await ReadRequiredJsonAsync<CollectionResponseDto>(response, "Create collection");
    }

    /// <summary>
    /// Updates an existing collection.
    /// </summary>
    public virtual async Task UpdateCollectionAsync(Guid id, UpdateCollectionDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/collections/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update collection");
    }

    /// <summary>
    /// Deletes a collection.
    /// </summary>
    public virtual async Task DeleteCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/collections/{id}", ct);
        await EnsureSuccessAsync(response, "Delete collection");
    }

    /// <summary>
    /// Gets deletion context (asset count + orphan count) for a collection.
    /// </summary>
    public virtual async Task<CollectionDeletionContextDto?> GetCollectionDeletionContextAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/collections/{id}/deletion-context", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return null;
        await EnsureSuccessAsync(response, "Get collection deletion context");
        return await response.Content.ReadFromJsonAsync<CollectionDeletionContextDto>(ct);
    }

    /// <summary>
    /// Gets the ACL entries for a collection (manager+).
    /// </summary>
    public virtual async Task<List<CollectionAclResponseDto>> GetCollectionAclsAsync(Guid collectionId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/collections/{collectionId}/acl", ct);
        await EnsureSuccessAsync(response, "Get collection ACLs");
        return await response.Content.ReadFromJsonAsync<List<CollectionAclResponseDto>>(ct) ?? new();
    }

    /// <summary>
    /// Sets (adds or updates) a user's access on a collection (manager+).
    /// </summary>
    public virtual async Task SetCollectionAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default)
    {
        var request = new { principalType, principalId, role };
        var response = await _http.PostAsJsonAsync($"/api/v1/collections/{collectionId}/acl", request, ct);
        await EnsureSuccessAsync(response, "Set collection access");
    }

    /// <summary>
    /// Revokes a user's access from a collection (manager+).
    /// </summary>
    public virtual async Task RevokeCollectionAccessAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default)
    {
        var url = $"/api/v1/collections/{collectionId}/acl/{Uri.EscapeDataString(principalType)}/{Uri.EscapeDataString(principalId)}";
        var response = await _http.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response, "Revoke collection access");
    }

    /// <summary>
    /// Searches for users that can be added to a collection's ACL (manager+).
    /// Excludes users who already have direct access.
    /// </summary>
    public virtual async Task<List<UserSearchResultDto>> SearchUsersForAclAsync(Guid collectionId, string? query = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/collections/{collectionId}/acl/users/search";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"?q={Uri.EscapeDataString(query)}";
        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Search users");
        return await response.Content.ReadFromJsonAsync<List<UserSearchResultDto>>(ct) ?? new();
    }

    #endregion

    #region Assets

    /// <summary>
    /// Gets assets in a specific collection with optional filtering.
    /// </summary>
    public virtual async Task<AssetListResponse> GetAssetsAsync(
        Guid collectionId,
        string? query = null,
        string? type = null,
        string sortBy = Constants.SortBy.CreatedDesc,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var url = $"/api/v1/assets/collection/{collectionId}?skip={skip}&take={take}&sortBy={sortBy}";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(type))
            url += $"&type={Uri.EscapeDataString(type)}";

        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Get assets");
        return await response.Content.ReadFromJsonAsync<AssetListResponse>(ct)
            ?? new AssetListResponse { CollectionId = collectionId, Total = 0, Items = new() };
    }

    /// <summary>
    /// Gets a single asset by ID.
    /// </summary>
    /// <returns>The asset, or null if not found.</returns>
    public virtual async Task<AssetResponseDto?> GetAssetAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/assets/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, "Get asset");
        return await response.Content.ReadFromJsonAsync<AssetResponseDto>(ct);
    }

    /// <summary>
    /// Updates an existing asset's metadata.
    /// </summary>
    public virtual async Task<AssetResponseDto> UpdateAssetAsync(Guid id, UpdateAssetDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/assets/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update asset");
        return await ReadRequiredJsonAsync<AssetResponseDto>(response, "Update asset");
    }

    /// <summary>
    /// Uploads a new asset to a collection via IFormFile (legacy fallback).
    /// Prefer <see cref="InitUploadAsync"/> + JS interop + <see cref="ConfirmUploadAsync"/>
    /// for large files, which bypasses SignalR and uploads directly to MinIO.
    /// </summary>
    public virtual async Task<AssetUploadResult> UploadAssetAsync(
        Guid collectionId,
        string title,
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", fileName);
        content.Add(new StringContent(collectionId.ToString()), "collectionId");
        content.Add(new StringContent(title), "title");

        var response = await _http.PostAsync("/api/v1/assets", content, ct);
        await EnsureSuccessAsync(response, "Upload asset");
        return await ReadRequiredJsonAsync<AssetUploadResult>(response, "Upload asset");
    }

    /// <summary>
    /// Step 1 of presigned upload: Creates an asset record and returns a presigned PUT URL.
    /// The browser then uploads the file directly to MinIO via JS interop.
    /// </summary>
    public virtual async Task<InitUploadResponse> InitUploadAsync(
        Guid? collectionId,
        string fileName,
        string contentType,
        long fileSize,
        string? title = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            collectionId,
            fileName,
            contentType,
            fileSize,
            title = title ?? Path.GetFileNameWithoutExtension(fileName)
        };

        var response = await _http.PostAsJsonAsync("/api/v1/assets/init-upload", request, ct);
        await EnsureSuccessAsync(response, "Init upload");
        return await ReadRequiredJsonAsync<InitUploadResponse>(response, "Init upload");
    }

    /// <summary>
    /// Step 2 of presigned upload: Confirms the file was uploaded to MinIO and triggers processing.
    /// </summary>
    /// <param name="force">When true, skip duplicate detection (admin override).</param>
    public virtual async Task<AssetUploadResult> ConfirmUploadAsync(Guid assetId, bool force = false, CancellationToken ct = default)
    {
        var url = force
            ? $"/api/v1/assets/{assetId}/confirm-upload?force=true"
            : $"/api/v1/assets/{assetId}/confirm-upload";
        var response = await _http.PostAsync(url, null, ct);
        await EnsureSuccessAsync(response, "Confirm upload");
        return await ReadRequiredJsonAsync<AssetUploadResult>(response, "Confirm upload");
    }

    /// <summary>
    /// Save an edited image as a new copy of the source asset.
    /// Returns a presigned URL for uploading the edited file.
    /// </summary>
    public virtual async Task<InitUploadResponse> SaveImageCopyAsync(
        Guid sourceAssetId, string contentType, long fileSize, string? title = null, Guid? collectionId = null, CancellationToken ct = default)
    {
        var request = new { contentType, fileSize, title, collectionId };
        var response = await _http.PostAsJsonAsync($"/api/v1/assets/{sourceAssetId}/save-copy", request, ct);
        await EnsureSuccessAsync(response, "Save image copy");
        return await ReadRequiredJsonAsync<InitUploadResponse>(response, "Save image copy");
    }

    /// <summary>
    /// Replace the original file of an asset with an edited version.
    /// Returns a presigned URL for uploading the replacement file.
    /// </summary>
    public virtual async Task<InitUploadResponse> ReplaceImageFileAsync(
        Guid assetId, string contentType, long fileSize, CancellationToken ct = default)
    {
        var request = new { contentType, fileSize };
        var response = await _http.PostAsJsonAsync($"/api/v1/assets/{assetId}/replace-file", request, ct);
        await EnsureSuccessAsync(response, "Replace image file");
        return await ReadRequiredJsonAsync<InitUploadResponse>(response, "Replace image file");
    }

    /// <summary>
    /// Optional parameters for <see cref="AssetHubApiClient.ApplyEditAsync"/>.
    /// </summary>
    public record ImageEditOptions(
        string? Title = null,
        string? EditDocument = null,
        Guid? DestinationCollectionId = null,
        Guid[]? PresetIds = null);

    /// <summary>
    /// Apply an image edit by uploading a rendered PNG and edit metadata via multipart form.
    /// </summary>
    public virtual async Task<ImageEditResultDto> ApplyEditAsync(
        Guid assetId, Stream renderedPng, string fileName, ImageEditSaveMode saveMode,
        ImageEditOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ImageEditOptions();

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(renderedPng), "file", fileName);
        content.Add(new StringContent(saveMode.ToString()), "SaveMode");

        if (!string.IsNullOrEmpty(options.Title))
            content.Add(new StringContent(options.Title), "Title");

        if (!string.IsNullOrEmpty(options.EditDocument))
            content.Add(new StringContent(options.EditDocument), "EditDocument");

        if (options.DestinationCollectionId.HasValue)
            content.Add(new StringContent(options.DestinationCollectionId.Value.ToString()), "DestinationCollectionId");

        if (options.PresetIds is { Length: > 0 })
        {
            foreach (var pid in options.PresetIds)
                content.Add(new StringContent(pid.ToString()), "PresetIds");
        }

        var response = await _http.PostAsync($"/api/v1/assets/{assetId}/edit", content, ct);
        await EnsureSuccessAsync(response, "Apply image edit");
        return await ReadRequiredJsonAsync<ImageEditResultDto>(response, "Apply image edit");
    }

    /// <summary>
    /// Deletes an asset. When fromCollectionId is set the asset is removed from that
    /// collection (and auto-deleted by the backend when orphaned). Without fromCollectionId
    /// a full permanent delete is performed.
    /// </summary>
    public virtual async Task DeleteAssetAsync(Guid id, Guid? fromCollectionId = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/assets/{id}";
        if (fromCollectionId.HasValue)
            url += $"?fromCollectionId={fromCollectionId.Value}";

        var response = await _http.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response, "Delete asset");
    }

    /// <summary>
    /// Bulk-deletes multiple assets, optionally from a specific collection.
    /// </summary>
    public virtual async Task<BulkDeleteAssetsResponse> BulkDeleteAssetsAsync(
        List<Guid> assetIds, Guid? fromCollectionId = null, CancellationToken ct = default)
    {
        var request = new { assetIds, fromCollectionId };
        var response = await _http.PostAsJsonAsync("/api/v1/assets/bulk-delete", request, ct);
        await EnsureSuccessAsync(response, "Bulk delete assets");
        return await ReadRequiredJsonAsync<BulkDeleteAssetsResponse>(response, "Bulk delete assets");
    }

    /// <summary>
    /// Returns deletion context for an asset: how many collections it belongs to
    /// and whether the current user can permanently delete it.
    /// </summary>
    public virtual async Task<AssetDeletionContextDto> GetAssetDeletionContextAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/assets/{id}/deletion-context", ct);
        await EnsureSuccessAsync(response, "Get asset deletion context");
        return await ReadRequiredJsonAsync<AssetDeletionContextDto>(response, "Get asset deletion context");
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
    public virtual async Task<ShareResponseDto> CreateShareAsync(
        Guid scopeId,
        string scopeType,
        DateTime? expiresAt = null,
        string? password = null,
        List<string>? notifyEmails = null,
        CancellationToken ct = default)
    {
        var dto = new
        {
            ScopeId = scopeId,
            ScopeType = scopeType,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            Password = password,
            NotifyEmails = notifyEmails
        };

        var response = await _http.PostAsJsonAsync("/api/v1/shares", dto, ct);
        await EnsureSuccessAsync(response, "Create share");
        return await ReadRequiredJsonAsync<ShareResponseDto>(response, "Create share");
    }

    /// <summary>
    /// Updates the password for an existing share.
    /// </summary>
    public virtual async Task UpdateSharePasswordAsync(Guid shareId, string newPassword, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/v1/shares/{shareId}/password", new { Password = newPassword }, ct);
        await EnsureSuccessAsync(response, "Update share password");
    }

    /// <summary>
    /// Retrieves the plaintext token for a share if available (admin-only).
    /// Returns empty string when the share predates token encryption.
    /// </summary>
    public virtual async Task<string> GetShareTokenAsync(Guid shareId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/shares/{shareId}/token", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return string.Empty;
        await EnsureSuccessAsync(response, "Get share token");
        var dto = await response.Content.ReadFromJsonAsync<ShareTokenResponse>(cancellationToken: ct);
        return dto?.Token ?? string.Empty;
    }

    /// <summary>
    /// Retrieves the plaintext password for a share if available (admin-only).
    /// Returns empty string when the share predates password encryption or has no password.
    /// </summary>
    public virtual async Task<string?> GetSharePasswordAsync(Guid shareId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/shares/{shareId}/password", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, "Get share password");
        var dto = await response.Content.ReadFromJsonAsync<SharePasswordResponse>(cancellationToken: ct);
        return dto?.Password;
    }

    /// <summary>
    /// Revokes a share link (user-level, for own shares).
    /// </summary>
    public virtual async Task RevokeShareAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/shares/{id}", ct);
        await EnsureSuccessAsync(response, "Revoke share");
    }

    /// <summary>
    /// Gets shared content by token (for public share pages).
    /// </summary>
    /// <remarks>
    /// Returns the raw response to allow handling password prompts and different content types.
    /// </remarks>
    public virtual async Task<HttpResponseMessage> GetSharedContentAsync(string token, string? password = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/shares/{Uri.EscapeDataString(token)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(password))
            request.Headers.TryAddWithoutValidation("X-Share-Password", password);
        return await _http.SendAsync(request, ct);
    }

    /// <summary>
    /// Requests a short-lived access token for a password-protected share.
    /// The access token can be used in query strings (img src, a href) without
    /// exposing the actual password.
    /// </summary>
    public virtual async Task<ShareAccessTokenResponse?> GetShareAccessTokenAsync(
        string token, string password, CancellationToken ct = default)
    {
        var url = $"/api/v1/shares/{Uri.EscapeDataString(token)}/access-token";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("X-Share-Password", password);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ShareAccessTokenResponse>(ct);
    }

    #endregion

    #region Presigned URLs

    /// <summary>
    /// Gets the download URL for an asset.
    /// </summary>
    public virtual Task<string> GetPresignedDownloadUrlAsync(Guid assetId, string objectKey, CancellationToken ct = default)
    {
        return Task.FromResult($"/api/v1/assets/{assetId}/download");
    }

    #endregion

    #region Admin

    /// <summary>
    /// Gets all shares in the system (admin only).
    /// </summary>
    public virtual async Task<AdminSharesResponse> GetAllSharesAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/shares?skip={skip}&take={take}", ct);
        await EnsureSuccessAsync(response, "Get all shares");
        return await response.Content.ReadFromJsonAsync<AdminSharesResponse>(ct) ?? new();
    }

    /// <summary>
    /// Revokes any share by ID (admin only).
    /// </summary>
    public virtual async Task RevokeShareAdminAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/shares/{id}", ct);
        await EnsureSuccessAsync(response, "Revoke share");
    }

    /// <summary>
    /// Permanently deletes a share (must be expired or revoked, admin only).
    /// </summary>
    public virtual async Task DeleteShareAdminAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/shares/{id}/permanent", ct);
        await EnsureSuccessAsync(response, "Delete share");
    }

    /// <summary>
    /// Bulk deletes all shares with the given status ("expired" or "revoked", admin only).
    /// Returns the number of shares deleted.
    /// </summary>
    public virtual async Task<int> BulkDeleteSharesByStatusAsync(string status, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/shares/bulk/{status}", ct);
        await EnsureSuccessAsync(response, $"Bulk delete {status} shares");
        return await response.Content.ReadFromJsonAsync<int>(ct);
    }

    /// <summary>
    /// Gets all collections with their ACLs (admin only).
    /// </summary>
    public virtual async Task<List<CollectionAccessDto>> GetCollectionAccessAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/admin/collections/access", ct);
        await EnsureSuccessAsync(response, "Get collection access");
        return await response.Content.ReadFromJsonAsync<List<CollectionAccessDto>>(ct) ?? new();
    }

    /// <summary>
    /// Adds a user or group to a collection's ACL (admin only).
    /// </summary>
    public virtual async Task AddCollectionAclAsync(
        Guid collectionId,
        string principalType,
        string principalId,
        string role,
        CancellationToken ct = default)
    {
        var request = new { principalType, principalId, role };
        var response = await _http.PostAsJsonAsync($"/api/v1/admin/collections/{collectionId}/acl", request, ct);
        await EnsureSuccessAsync(response, "Add collection access");
    }

    /// <summary>
    /// Updates a user or group's role in a collection's ACL (admin only).
    /// </summary>
    public virtual async Task UpdateCollectionAclAsync(
        Guid collectionId,
        string principalType,
        string principalId,
        string role,
        CancellationToken ct = default)
    {
        var request = new { principalType, principalId, role };
        var response = await _http.PostAsJsonAsync($"/api/v1/admin/collections/{collectionId}/acl", request, ct);
        await EnsureSuccessAsync(response, "Update collection access");
    }

    /// <summary>
    /// Removes a user or group from a collection's ACL (admin only).
    /// </summary>
    public virtual async Task RemoveCollectionAclAsync(Guid collectionId, string principalId, string principalType, CancellationToken ct = default)
    {
        var url = $"/api/v1/admin/collections/{collectionId}/acl/{Uri.EscapeDataString(principalId)}?principalType={principalType}";
        var response = await _http.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response, "Remove collection access");
    }

    /// <summary>
    /// Bulk-deletes multiple collections (admin only).
    /// </summary>
    public virtual async Task<BulkDeleteCollectionsResponse> BulkDeleteCollectionsAsync(List<Guid> collectionIds, bool deleteAssets = true, CancellationToken ct = default)
    {
        var request = new { collectionIds, deleteAssets };
        var response = await _http.PostAsJsonAsync("/api/v1/admin/collections/bulk-delete", request, ct);
        await EnsureSuccessAsync(response, "Bulk delete collections");
        return await ReadRequiredJsonAsync<BulkDeleteCollectionsResponse>(response, "Bulk delete collections");
    }

    /// <summary>
    /// Bulk sets access on multiple collections (admin only).
    /// </summary>
    public virtual async Task<BulkSetCollectionAccessResponse> BulkSetCollectionAccessAsync(
        List<Guid> collectionIds, string principalId, string role, CancellationToken ct = default)
    {
        var request = new { collectionIds, principalType = Constants.PrincipalTypes.User, principalId, role };
        var response = await _http.PostAsJsonAsync("/api/v1/admin/collections/bulk-set-access", request, ct);
        await EnsureSuccessAsync(response, "Bulk set collection access");
        return await ReadRequiredJsonAsync<BulkSetCollectionAccessResponse>(response, "Bulk set collection access");
    }

    /// <summary>
    /// Gets all users who have access to collections (admin only).
    /// </summary>
    public virtual async Task<List<UserAccessSummaryDto>> GetUsersAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/admin/users", ct);
        await EnsureSuccessAsync(response, "Get users");
        return await response.Content.ReadFromJsonAsync<List<UserAccessSummaryDto>>(ct) ?? new();
    }

    /// <summary>
    /// Gets all users from Keycloak realm (admin only).
    /// </summary>
    public virtual async Task<List<KeycloakUserDto>> GetKeycloakUsersAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/admin/keycloak-users", ct);
        await EnsureSuccessAsync(response, "Get Keycloak users");
        return await response.Content.ReadFromJsonAsync<List<KeycloakUserDto>>(ct) ?? new();
    }

    /// <summary>
    /// Gets paginated users from Keycloak with filtering, sorting, and category counts (admin only).
    /// </summary>
    public virtual async Task<PaginatedKeycloakUsersResponse> GetKeycloakUsersPaginatedAsync(
        string? search = null, string? category = null,
        string? sortBy = null, bool sortDesc = false,
        int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var url = $"/api/v1/admin/keycloak-users/paginated?skip={skip}&take={take}";
        if (!string.IsNullOrEmpty(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrEmpty(category)) url += $"&category={Uri.EscapeDataString(category)}";
        if (!string.IsNullOrEmpty(sortBy)) url += $"&sortBy={Uri.EscapeDataString(sortBy)}";
        if (sortDesc) url += "&sortDesc=true";
        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Get Keycloak users (paginated)");
        return await response.Content.ReadFromJsonAsync<PaginatedKeycloakUsersResponse>(ct)
            ?? new PaginatedKeycloakUsersResponse();
    }

    /// <summary>
    /// Creates a new user in Keycloak (admin only).
    /// </summary>
    public virtual async Task<CreateUserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/admin/users", request, ct);
        await EnsureSuccessAsync(response, "Create user");
        return await ReadRequiredJsonAsync<CreateUserResponse>(response, "Create user");
    }

    /// <summary>
    /// Sends a password reset email to a user via Keycloak (admin only).
    /// </summary>
    public virtual async Task SendPasswordResetEmailAsync(string userId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/users/{userId}/reset-password", null, ct);
        await EnsureSuccessAsync(response, "Send password reset email");
    }

    /// <summary>
    /// Deletes a user from Keycloak and cleans up app data (admin only).
    /// </summary>
    public virtual async Task<DeleteUserResponse> DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/users/{userId}", ct);
        await EnsureSuccessAsync(response, "Delete user");
        return await ReadRequiredJsonAsync<DeleteUserResponse>(response, "Delete user");
    }

    /// <summary>
    /// Syncs deleted users — detects and cleans up Keycloak users that no longer exist (admin only).
    /// </summary>
    public virtual async Task<UserSyncResult> SyncDeletedUsersAsync(bool dryRun = false, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/users/sync?dryRun={dryRun.ToString().ToLowerInvariant()}", null, ct);
        await EnsureSuccessAsync(response, "Sync deleted users");
        return await ReadRequiredJsonAsync<UserSyncResult>(response, "Sync deleted users");
    }

    /// <summary>
    /// Gets all audit events (admin only).
    /// </summary>
    public virtual async Task<List<AuditEventDto>> GetAuditEventsAsync(int take = 200, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/audit?take={take}", ct);
        await EnsureSuccessAsync(response, "Get audit events");
        return await response.Content.ReadFromJsonAsync<List<AuditEventDto>>(ct) ?? new();
    }

    /// <summary>
    /// Gets audit events with pagination and filtering (admin only).
    /// </summary>
    public virtual async Task<AuditQueryResponse> GetAuditEventsPaginatedAsync(
        int pageSize = 50,
        DateTime? cursor = null,
        string? eventType = null,
        string? targetType = null,
        string? actorUserId = null,
        CancellationToken ct = default)
    {
        var queryParams = new List<string> { $"pageSize={pageSize}" };
        if (cursor.HasValue)
            queryParams.Add($"cursor={cursor.Value:O}");
        if (!string.IsNullOrWhiteSpace(eventType))
            queryParams.Add($"eventType={Uri.EscapeDataString(eventType)}");
        if (!string.IsNullOrWhiteSpace(targetType))
            queryParams.Add($"targetType={Uri.EscapeDataString(targetType)}");
        if (!string.IsNullOrWhiteSpace(actorUserId))
            queryParams.Add($"actorUserId={Uri.EscapeDataString(actorUserId)}");

        var url = $"/api/v1/admin/audit/paginated?{string.Join("&", queryParams)}";
        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Get audit events paginated");
        return await response.Content.ReadFromJsonAsync<AuditQueryResponse>(ct) ?? new();
    }

    /// <summary>
    /// Gets all export presets (available to all authenticated users).
    /// </summary>
    public virtual async Task<List<ExportPresetDto>> GetExportPresetsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/export-presets", ct);
        await EnsureSuccessAsync(response, "Get export presets");
        return await response.Content.ReadFromJsonAsync<List<ExportPresetDto>>(ct) ?? new();
    }

    /// <summary>
    /// Gets a single export preset by ID (admin only).
    /// </summary>
    public virtual async Task<ExportPresetDto?> GetExportPresetAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/export-presets/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, "Get export preset");
        return await response.Content.ReadFromJsonAsync<ExportPresetDto>(ct);
    }

    /// <summary>
    /// Creates a new export preset (admin only).
    /// </summary>
    public virtual async Task<ExportPresetDto> CreateExportPresetAsync(CreateExportPresetDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/admin/export-presets", dto, ct);
        await EnsureSuccessAsync(response, "Create export preset");
        return await ReadRequiredJsonAsync<ExportPresetDto>(response, "Create export preset");
    }

    /// <summary>
    /// Updates an existing export preset (admin only).
    /// </summary>
    public virtual async Task UpdateExportPresetAsync(Guid id, UpdateExportPresetDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/admin/export-presets/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update export preset");
    }

    /// <summary>
    /// Deletes an export preset (admin only).
    /// </summary>
    public virtual async Task DeleteExportPresetAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/export-presets/{id}", ct);
        await EnsureSuccessAsync(response, "Delete export preset");
    }

    #endregion

    #region Personal Access Tokens

    /// <summary>
    /// Lists the current user's personal access tokens (active + revoked + expired), newest first.
    /// </summary>
    public virtual async Task<List<PersonalAccessTokenDto>> GetMyPersonalAccessTokensAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/me/personal-access-tokens", ct);
        await EnsureSuccessAsync(response, "List personal access tokens");
        return await response.Content.ReadFromJsonAsync<List<PersonalAccessTokenDto>>(ct) ?? new();
    }

    /// <summary>
    /// Mints a new personal access token. The plaintext token is present in the response
    /// exactly once — it is not recoverable from later GETs.
    /// </summary>
    public virtual async Task<CreatedPersonalAccessTokenDto> CreatePersonalAccessTokenAsync(
        CreatePersonalAccessTokenRequest request,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/me/personal-access-tokens", request, ct);
        await EnsureSuccessAsync(response, "Create personal access token");
        return await ReadRequiredJsonAsync<CreatedPersonalAccessTokenDto>(response, "Create personal access token");
    }

    /// <summary>
    /// Revokes one of the current user's personal access tokens. Idempotent.
    /// </summary>
    public virtual async Task RevokePersonalAccessTokenAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/me/personal-access-tokens/{id}", ct);
        await EnsureSuccessAsync(response, "Revoke personal access token");
    }

    #endregion

    #region Notifications

    public virtual async Task<NotificationListResponse> GetNotificationsAsync(
        bool unreadOnly = false, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"/api/v1/notifications?unreadOnly={unreadOnly.ToString().ToLowerInvariant()}&skip={skip}&take={take}",
            ct);
        await EnsureSuccessAsync(response, "Get notifications");
        return await response.Content.ReadFromJsonAsync<NotificationListResponse>(ct)
            ?? throw new ApiException("Failed to deserialize notifications", System.Net.HttpStatusCode.InternalServerError);
    }

    public virtual async Task<int> GetNotificationUnreadCountAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/notifications/unread-count", ct);
        await EnsureSuccessAsync(response, "Get notification unread count");
        var body = await response.Content.ReadFromJsonAsync<NotificationUnreadCountDto>(ct);
        return body?.Count ?? 0;
    }

    public virtual async Task MarkNotificationReadAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/notifications/{id}/read", null, ct);
        await EnsureSuccessAsync(response, "Mark notification read");
    }

    public virtual async Task<int> MarkAllNotificationsReadAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("/api/v1/notifications/read-all", null, ct);
        await EnsureSuccessAsync(response, "Mark all notifications read");
        return await response.Content.ReadFromJsonAsync<int>(ct);
    }

    public virtual async Task DeleteNotificationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/notifications/{id}", ct);
        await EnsureSuccessAsync(response, "Delete notification");
    }

    public virtual async Task<NotificationPreferencesDto> GetNotificationPreferencesAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/notifications/preferences", ct);
        await EnsureSuccessAsync(response, "Get notification preferences");
        return await response.Content.ReadFromJsonAsync<NotificationPreferencesDto>(ct)
            ?? throw new ApiException("Failed to deserialize notification preferences", System.Net.HttpStatusCode.InternalServerError);
    }

    public virtual async Task<NotificationPreferencesDto> UpdateNotificationPreferencesAsync(
        UpdateNotificationPreferencesDto dto, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync("/api/v1/notifications/preferences", dto, ct);
        await EnsureSuccessAsync(response, "Update notification preferences");
        return await response.Content.ReadFromJsonAsync<NotificationPreferencesDto>(ct)
            ?? throw new ApiException("Failed to deserialize notification preferences", System.Net.HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Migrations (Admin)

    public virtual async Task<MigrationListResponse> GetMigrationsAsync(int skip = 0, int take = 20, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/migrations?skip={skip}&take={take}", ct);
        await EnsureSuccessAsync(response, "Get migrations");
        return await response.Content.ReadFromJsonAsync<MigrationListResponse>(ct)
            ?? new() { Migrations = [], TotalCount = 0 };
    }

    public virtual async Task<MigrationResponseDto> GetMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/migrations/{id}", ct);
        await EnsureSuccessAsync(response, "Get migration");
        return await response.Content.ReadFromJsonAsync<MigrationResponseDto>(ct)
            ?? throw new ApiException("Failed to deserialize migration", System.Net.HttpStatusCode.InternalServerError);
    }

    public virtual async Task<MigrationResponseDto> CreateMigrationAsync(CreateMigrationDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/admin/migrations", dto, ct);
        await EnsureSuccessAsync(response, "Create migration");
        return await response.Content.ReadFromJsonAsync<MigrationResponseDto>(ct)
            ?? throw new ApiException("Failed to deserialize migration", System.Net.HttpStatusCode.InternalServerError);
    }

    public virtual async Task UploadMigrationManifestAsync(Guid id, Stream csvStream, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(csvStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(streamContent, "file", fileName);

        var response = await _http.PostAsync($"/api/v1/admin/migrations/{id}/manifest", content, ct);
        await EnsureSuccessAsync(response, "Upload migration manifest");
    }

    public virtual async Task UploadMigrationFilesAsync(Guid id, IEnumerable<(string FileName, Stream Stream, string ContentType)> files, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        foreach (var (fileName, stream, contentType) in files)
        {
            var streamContent = new StreamContent(stream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "files", fileName);
        }

        var response = await _http.PostAsync($"/api/v1/admin/migrations/{id}/files", content, ct);
        await EnsureSuccessAsync(response, "Upload migration files");
    }

    public virtual async Task StartMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/migrations/{id}/start", null, ct);
        await EnsureSuccessAsync(response, "Start migration");
    }

    public virtual async Task StartMigrationS3ScanAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/migrations/{id}/s3/scan", null, ct);
        await EnsureSuccessAsync(response, "Start S3 scan");
    }

    public virtual async Task CancelMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/migrations/{id}/cancel", null, ct);
        await EnsureSuccessAsync(response, "Cancel migration");
    }

    public virtual async Task RetryFailedMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/migrations/{id}/retry", null, ct);
        await EnsureSuccessAsync(response, "Retry failed migration items");
    }

    public virtual async Task<MigrationProgressDto> GetMigrationProgressAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/migrations/{id}/progress", ct);
        await EnsureSuccessAsync(response, "Get migration progress");
        return await response.Content.ReadFromJsonAsync<MigrationProgressDto>(ct)
            ?? throw new ApiException("Failed to deserialize migration progress", System.Net.HttpStatusCode.InternalServerError);
    }

    public virtual async Task<MigrationItemListResponse> GetMigrationItemsAsync(Guid id, string? statusFilter = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var url = $"/api/v1/admin/migrations/{id}/items?skip={skip}&take={take}";
        if (!string.IsNullOrEmpty(statusFilter))
            url += $"&status={statusFilter}";

        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Get migration items");
        return await response.Content.ReadFromJsonAsync<MigrationItemListResponse>(ct)
            ?? new() { Items = [], TotalCount = 0 };
    }

    public virtual async Task DeleteMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/migrations/{id}", ct);
        await EnsureSuccessAsync(response, "Delete migration");
    }

    public virtual async Task<Stream> DownloadMigrationOutcomeAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/migrations/{id}/outcome.csv", ct);
        await EnsureSuccessAsync(response, "Download migration outcome");
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public virtual async Task UnstageMigrationItemAsync(Guid migrationId, Guid itemId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/migrations/{migrationId}/items/{itemId}/unstage", ct);
        await EnsureSuccessAsync(response, "Unstage migration item");
    }

    public virtual async Task<int> BulkDeleteMigrationsAsync(string filter, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/migrations/bulk?filter={Uri.EscapeDataString(filter)}", ct);
        await EnsureSuccessAsync(response, "Bulk delete migrations");
        return await response.Content.ReadFromJsonAsync<int>(ct);
    }

    #endregion

    #region Asset Collections (Multi-Collection)

    /// <summary>
    /// Gets all collections an asset belongs to.
    /// </summary>
    public virtual async Task<List<AssetCollectionDto>> GetAssetCollectionsAsync(Guid assetId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/assets/{assetId}/collections", ct);
        await EnsureSuccessAsync(response, "Get asset collections");
        return await response.Content.ReadFromJsonAsync<List<AssetCollectionDto>>(ct) ?? new();
    }

    /// <summary>
    /// Gets derivative assets created from a source asset via image editing.
    /// </summary>
    public virtual async Task<List<AssetDerivativeDto>> GetAssetDerivativesAsync(Guid assetId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/assets/{assetId}/derivatives", ct);
        await EnsureSuccessAsync(response, "Get asset derivatives");
        return await response.Content.ReadFromJsonAsync<List<AssetDerivativeDto>>(ct) ?? new();
    }

    /// <summary>
    /// Adds an asset to a collection.
    /// </summary>
    public virtual async Task AddAssetToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/assets/{assetId}/collections/{collectionId}", null, ct);
        await EnsureSuccessAsync(response, "Add asset to collection");
    }

    /// <summary>
    /// Removes an asset from a collection.
    /// </summary>
    public virtual async Task RemoveAssetFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/assets/{assetId}/collections/{collectionId}", ct);
        await EnsureSuccessAsync(response, "Remove asset from collection");
    }

    #endregion

    #region Metadata Schemas

    public virtual async Task<List<MetadataSchemaDto>> GetMetadataSchemasAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/metadata-schemas", ct);
        await EnsureSuccessAsync(response, "Get metadata schemas");
        return await response.Content.ReadFromJsonAsync<List<MetadataSchemaDto>>(ct) ?? new();
    }

    public virtual async Task<MetadataSchemaDto?> GetMetadataSchemaAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/metadata-schemas/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "Get metadata schema");
        return await response.Content.ReadFromJsonAsync<MetadataSchemaDto>(ct);
    }

    public virtual async Task<List<MetadataSchemaDto>> GetApplicableMetadataSchemasAsync(string? assetType = null, Guid? collectionId = null, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(assetType)) qs.Add($"assetType={Uri.EscapeDataString(assetType)}");
        if (collectionId.HasValue) qs.Add($"collectionId={collectionId}");
        var url = "/api/v1/metadata-schemas/applicable" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Get applicable metadata schemas");
        return await response.Content.ReadFromJsonAsync<List<MetadataSchemaDto>>(ct) ?? new();
    }

    public virtual async Task<MetadataSchemaDto> CreateMetadataSchemaAsync(CreateMetadataSchemaDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/admin/metadata-schemas", dto, ct);
        await EnsureSuccessAsync(response, "Create metadata schema");
        return await ReadRequiredJsonAsync<MetadataSchemaDto>(response, "Create metadata schema");
    }

    public virtual async Task<MetadataSchemaDto> UpdateMetadataSchemaAsync(Guid id, UpdateMetadataSchemaDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/admin/metadata-schemas/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update metadata schema");
        return await ReadRequiredJsonAsync<MetadataSchemaDto>(response, "Update metadata schema");
    }

    public virtual async Task DeleteMetadataSchemaAsync(Guid id, bool force = false, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/metadata-schemas/{id}?force={force}", ct);
        await EnsureSuccessAsync(response, "Delete metadata schema");
    }

    #endregion

    #region Taxonomies

    public virtual async Task<List<TaxonomySummaryDto>> GetTaxonomiesAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/taxonomies", ct);
        await EnsureSuccessAsync(response, "Get taxonomies");
        return await response.Content.ReadFromJsonAsync<List<TaxonomySummaryDto>>(ct) ?? new();
    }

    public virtual async Task<TaxonomyDto?> GetTaxonomyAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/taxonomies/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "Get taxonomy");
        return await response.Content.ReadFromJsonAsync<TaxonomyDto>(ct);
    }

    public virtual async Task<TaxonomyDto> CreateTaxonomyAsync(CreateTaxonomyDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/admin/taxonomies", dto, ct);
        await EnsureSuccessAsync(response, "Create taxonomy");
        return await ReadRequiredJsonAsync<TaxonomyDto>(response, "Create taxonomy");
    }

    public virtual async Task<TaxonomyDto> UpdateTaxonomyAsync(Guid id, UpdateTaxonomyDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/admin/taxonomies/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update taxonomy");
        return await ReadRequiredJsonAsync<TaxonomyDto>(response, "Update taxonomy");
    }

    public virtual async Task<TaxonomyDto> ReplaceTaxonomyTermsAsync(Guid id, List<UpsertTaxonomyTermDto> terms, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/v1/admin/taxonomies/{id}/terms", terms, ct);
        await EnsureSuccessAsync(response, "Replace taxonomy terms");
        return await ReadRequiredJsonAsync<TaxonomyDto>(response, "Replace taxonomy terms");
    }

    public virtual async Task DeleteTaxonomyAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/taxonomies/{id}", ct);
        await EnsureSuccessAsync(response, "Delete taxonomy");
    }

    #endregion

    #region Asset Metadata

    public virtual async Task<List<AssetMetadataValueDto>> GetAssetMetadataAsync(Guid assetId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/assets/{assetId}/metadata", ct);
        await EnsureSuccessAsync(response, "Get asset metadata");
        return await response.Content.ReadFromJsonAsync<List<AssetMetadataValueDto>>(ct) ?? new();
    }

    public virtual async Task<List<AssetMetadataValueDto>> SetAssetMetadataAsync(Guid assetId, SetAssetMetadataDto dto, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/v1/assets/{assetId}/metadata", dto, ct);
        await EnsureSuccessAsync(response, "Set asset metadata");
        return await ReadRequiredJsonAsync<List<AssetMetadataValueDto>>(response, "Set asset metadata");
    }

    #endregion

    #region Asset Search

    public virtual async Task<AssetSearchResponse> SearchAssetsAsync(AssetSearchRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/assets/search", request, ct);
        await EnsureSuccessAsync(response, "Search assets");
        return await ReadRequiredJsonAsync<AssetSearchResponse>(response, "Search assets");
    }

    #endregion

    #region Saved Searches

    public virtual async Task<List<SavedSearchDto>> GetSavedSearchesAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/saved-searches", ct);
        await EnsureSuccessAsync(response, "Get saved searches");
        return await response.Content.ReadFromJsonAsync<List<SavedSearchDto>>(ct) ?? new();
    }

    public virtual async Task<SavedSearchDto?> GetSavedSearchAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/saved-searches/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "Get saved search");
        return await response.Content.ReadFromJsonAsync<SavedSearchDto>(ct);
    }

    public virtual async Task<SavedSearchDto> CreateSavedSearchAsync(CreateSavedSearchDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/saved-searches", dto, ct);
        await EnsureSuccessAsync(response, "Create saved search");
        return await ReadRequiredJsonAsync<SavedSearchDto>(response, "Create saved search");
    }

    public virtual async Task<SavedSearchDto> UpdateSavedSearchAsync(Guid id, UpdateSavedSearchDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/saved-searches/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update saved search");
        return await ReadRequiredJsonAsync<SavedSearchDto>(response, "Update saved search");
    }

    public virtual async Task DeleteSavedSearchAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/saved-searches/{id}", ct);
        await EnsureSuccessAsync(response, "Delete saved search");
    }

    #endregion

    #region Admin Trash

    public virtual async Task<TrashListResponse> GetTrashAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/trash?skip={skip}&take={take}", ct);
        await EnsureSuccessAsync(response, "Get trash");
        return await ReadRequiredJsonAsync<TrashListResponse>(response, "Get trash");
    }

    public virtual async Task RestoreFromTrashAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/trash/{id}/restore", content: null, ct);
        await EnsureSuccessAsync(response, "Restore from trash");
    }

    public virtual async Task PurgeFromTrashAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/trash/{id}", ct);
        await EnsureSuccessAsync(response, "Purge from trash");
    }

    public virtual async Task<EmptyTrashResponse> EmptyTrashAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("/api/v1/admin/trash/empty", content: null, ct);
        await EnsureSuccessAsync(response, "Empty trash");
        return await ReadRequiredJsonAsync<EmptyTrashResponse>(response, "Empty trash");
    }

    #endregion

    #region Asset Versions

    public virtual async Task<List<AssetVersionDto>> GetAssetVersionsAsync(Guid assetId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/assets/{assetId}/versions", ct);
        await EnsureSuccessAsync(response, "Get asset versions");
        return await response.Content.ReadFromJsonAsync<List<AssetVersionDto>>(ct) ?? new();
    }

    public virtual async Task<AssetVersionDto> RestoreAssetVersionAsync(Guid assetId, int versionNumber, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/assets/{assetId}/versions/{versionNumber}/restore", content: null, ct);
        await EnsureSuccessAsync(response, "Restore asset version");
        return await ReadRequiredJsonAsync<AssetVersionDto>(response, "Restore asset version");
    }

    public virtual async Task PruneAssetVersionAsync(Guid assetId, int versionNumber, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/assets/{assetId}/versions/{versionNumber}", ct);
        await EnsureSuccessAsync(response, "Prune asset version");
    }

    #endregion

    #region Asset Comments

    public virtual async Task<List<AssetCommentResponseDto>> GetAssetCommentsAsync(Guid assetId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/assets/{assetId}/comments", ct);
        await EnsureSuccessAsync(response, "Get asset comments");
        return await response.Content.ReadFromJsonAsync<List<AssetCommentResponseDto>>(ct) ?? new();
    }

    public virtual async Task<AssetCommentResponseDto> CreateAssetCommentAsync(
        Guid assetId, CreateAssetCommentDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"/api/v1/assets/{assetId}/comments", dto, ct);
        await EnsureSuccessAsync(response, "Create asset comment");
        return await ReadRequiredJsonAsync<AssetCommentResponseDto>(response, "Create asset comment");
    }

    public virtual async Task<AssetCommentResponseDto> UpdateAssetCommentAsync(
        Guid assetId, Guid commentId, UpdateAssetCommentDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/assets/{assetId}/comments/{commentId}", dto, ct);
        await EnsureSuccessAsync(response, "Update asset comment");
        return await ReadRequiredJsonAsync<AssetCommentResponseDto>(response, "Update asset comment");
    }

    public virtual async Task DeleteAssetCommentAsync(Guid assetId, Guid commentId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/assets/{assetId}/comments/{commentId}", ct);
        await EnsureSuccessAsync(response, "Delete asset comment");
    }

    #endregion

    #region Asset Workflow (T3-WF-01)

    public virtual async Task<AssetWorkflowResponseDto> GetAssetWorkflowAsync(Guid assetId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/assets/{assetId}/workflow", ct);
        await EnsureSuccessAsync(response, "Get asset workflow");
        return await ReadRequiredJsonAsync<AssetWorkflowResponseDto>(response, "Get asset workflow");
    }

    public virtual Task<AssetWorkflowResponseDto> SubmitAssetForReviewAsync(Guid assetId, string? reason, CancellationToken ct = default)
        => PostWorkflowAsync(assetId, "submit", new WorkflowActionDto { Reason = reason }, ct);

    public virtual Task<AssetWorkflowResponseDto> ApproveAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
        => PostWorkflowAsync(assetId, "approve", new WorkflowActionDto { Reason = reason }, ct);

    public virtual async Task<AssetWorkflowResponseDto> RejectAssetAsync(Guid assetId, string reason, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/v1/assets/{assetId}/workflow/reject",
            new WorkflowRejectDto { Reason = reason }, ct);
        await EnsureSuccessAsync(response, "Reject asset");
        return await ReadRequiredJsonAsync<AssetWorkflowResponseDto>(response, "Reject asset");
    }

    public virtual Task<AssetWorkflowResponseDto> PublishAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
        => PostWorkflowAsync(assetId, "publish", new WorkflowActionDto { Reason = reason }, ct);

    public virtual Task<AssetWorkflowResponseDto> UnpublishAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
        => PostWorkflowAsync(assetId, "unpublish", new WorkflowActionDto { Reason = reason }, ct);

    private async Task<AssetWorkflowResponseDto> PostWorkflowAsync(
        Guid assetId, string action, WorkflowActionDto dto, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/v1/assets/{assetId}/workflow/{action}", dto, ct);
        await EnsureSuccessAsync(response, $"{action} asset workflow");
        return await ReadRequiredJsonAsync<AssetWorkflowResponseDto>(response, $"{action} asset workflow");
    }

    #endregion

    #region Webhooks (T3-INT-01)

    public virtual async Task<List<WebhookResponseDto>> GetWebhooksAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/admin/webhooks", ct);
        await EnsureSuccessAsync(response, "Get webhooks");
        return await response.Content.ReadFromJsonAsync<List<WebhookResponseDto>>(ct) ?? new();
    }

    public virtual async Task<CreatedWebhookDto> CreateWebhookAsync(CreateWebhookDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/admin/webhooks", dto, ct);
        await EnsureSuccessAsync(response, "Create webhook");
        return await ReadRequiredJsonAsync<CreatedWebhookDto>(response, "Create webhook");
    }

    public virtual async Task<WebhookResponseDto> UpdateWebhookAsync(Guid id, UpdateWebhookDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/admin/webhooks/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update webhook");
        return await ReadRequiredJsonAsync<WebhookResponseDto>(response, "Update webhook");
    }

    public virtual async Task DeleteWebhookAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/webhooks/{id}", ct);
        await EnsureSuccessAsync(response, "Delete webhook");
    }

    public virtual async Task<CreatedWebhookDto> RotateWebhookSecretAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/webhooks/{id}/rotate-secret", content: null, ct);
        await EnsureSuccessAsync(response, "Rotate webhook secret");
        return await ReadRequiredJsonAsync<CreatedWebhookDto>(response, "Rotate webhook secret");
    }

    public virtual async Task<WebhookDeliveryResponseDto> SendWebhookTestAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/webhooks/{id}/test", content: null, ct);
        await EnsureSuccessAsync(response, "Send webhook test");
        return await ReadRequiredJsonAsync<WebhookDeliveryResponseDto>(response, "Send webhook test");
    }

    public virtual async Task<List<WebhookDeliveryResponseDto>> GetWebhookDeliveriesAsync(
        Guid id, int take = 50, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/v1/admin/webhooks/{id}/deliveries?take={take}", ct);
        await EnsureSuccessAsync(response, "Get webhook deliveries");
        return await response.Content.ReadFromJsonAsync<List<WebhookDeliveryResponseDto>>(ct) ?? new();
    }

    #endregion

    #region Brands (T4-BP-01)

    public virtual async Task<List<BrandResponseDto>> GetBrandsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/admin/brands", ct);
        await EnsureSuccessAsync(response, "Get brands");
        return await response.Content.ReadFromJsonAsync<List<BrandResponseDto>>(ct) ?? new();
    }

    public virtual async Task<BrandResponseDto> CreateBrandAsync(CreateBrandDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/admin/brands", dto, ct);
        await EnsureSuccessAsync(response, "Create brand");
        return await ReadRequiredJsonAsync<BrandResponseDto>(response, "Create brand");
    }

    public virtual async Task<BrandResponseDto> UpdateBrandAsync(Guid id, UpdateBrandDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/v1/admin/brands/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update brand");
        return await ReadRequiredJsonAsync<BrandResponseDto>(response, "Update brand");
    }

    public virtual async Task DeleteBrandAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/brands/{id}", ct);
        await EnsureSuccessAsync(response, "Delete brand");
    }

    public virtual async Task<BrandResponseDto> UploadBrandLogoAsync(
        Guid id, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        var response = await _http.PostAsync($"/api/v1/admin/brands/{id}/logo", form, ct);
        await EnsureSuccessAsync(response, "Upload brand logo");
        return await ReadRequiredJsonAsync<BrandResponseDto>(response, "Upload brand logo");
    }

    public virtual async Task RemoveBrandLogoAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/admin/brands/{id}/logo", ct);
        await EnsureSuccessAsync(response, "Remove brand logo");
    }

    #endregion

    #region Guest invitations (T4-GUEST-01)

    public virtual async Task<List<GuestInvitationResponseDto>> GetGuestInvitationsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/v1/admin/guest-invitations", ct);
        await EnsureSuccessAsync(response, "Get guest invitations");
        return await response.Content.ReadFromJsonAsync<List<GuestInvitationResponseDto>>(ct) ?? new();
    }

    public virtual async Task<CreatedGuestInvitationDto> CreateGuestInvitationAsync(
        CreateGuestInvitationDto dto, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/admin/guest-invitations", dto, ct);
        await EnsureSuccessAsync(response, "Create guest invitation");
        return await ReadRequiredJsonAsync<CreatedGuestInvitationDto>(response, "Create guest invitation");
    }

    public virtual async Task RevokeGuestInvitationAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/admin/guest-invitations/{id}/revoke", content: null, ct);
        await EnsureSuccessAsync(response, "Revoke guest invitation");
    }

    /// <summary>
    /// Anonymous redeem of a guest invitation magic-link token. Triggered
    /// from the public <c>/guest-accept</c> landing page.
    /// </summary>
    public virtual async Task<AcceptGuestInvitationResponseDto> AcceptGuestInvitationAsync(
        string token, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "/api/v1/guest-invitations/accept", new { Token = token }, ct);
        await EnsureSuccessAsync(response, "Accept guest invitation");
        return await ReadRequiredJsonAsync<AcceptGuestInvitationResponseDto>(response, "Accept guest invitation");
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

    /// <summary>
    /// The structured error code from the API (e.g. "DUPLICATE_ASSET", "NOT_FOUND").
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Additional structured details from the error response.
    /// </summary>
    public Dictionary<string, string>? Details { get; }

    public ApiException(string message, System.Net.HttpStatusCode statusCode, string? errorCode = null, Dictionary<string, string>? details = null) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details;
    }

    public override string ToString()
    {
        return $"ApiException: {Message} (HTTP {(int)StatusCode}, Code={ErrorCode})";
    }
}
