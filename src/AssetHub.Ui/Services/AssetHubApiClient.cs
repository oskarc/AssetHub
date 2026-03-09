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
            var root = doc.RootElement;

            // Standard {"error": "..."} format
            if (root.TryGetProperty("error", out var errorProp))
                return errorProp.GetString() ?? errorContent;

            // Standard {"message": "..."} format
            if (root.TryGetProperty("message", out var messageProp))
                return messageProp.GetString() ?? errorContent;

            // RFC 7807 ProblemDetails format {"title": "...", "detail": "..."}
            if (root.TryGetProperty("detail", out var detailProp))
            {
                var detail = detailProp.GetString();
                if (!string.IsNullOrWhiteSpace(detail))
                    return detail;
            }
            if (root.TryGetProperty("title", out var titleProp))
            {
                var title = titleProp.GetString();
                if (!string.IsNullOrWhiteSpace(title))
                    return title;
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

    #region Dashboard

    /// <summary>
    /// Gets aggregated dashboard data scoped to the current user's role.
    /// </summary>
    public async Task<DashboardDto?> GetDashboardAsync()
    {
        var response = await _http.GetAsync("/api/dashboard");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<DashboardDto>();
    }

    #endregion

    #region Collections

    /// <summary>
    /// Gets all collections.
    /// </summary>
    /// <returns>List of collections (empty if none found).</returns>
    public virtual async Task<List<CollectionResponseDto>> GetCollectionsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/collections", ct);
        await EnsureSuccessAsync(response, "Get collections");
        return await response.Content.ReadFromJsonAsync<List<CollectionResponseDto>>(ct) ?? new();
    }

    /// <summary>
    /// Gets a single collection by ID.
    /// </summary>
    /// <returns>The collection, or null if not found.</returns>
    public virtual async Task<CollectionResponseDto?> GetCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/collections/{id}", ct);
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
        var response = await _http.PostAsJsonAsync("/api/collections", dto, ct);
        await EnsureSuccessAsync(response, "Create collection");
        return await ReadRequiredJsonAsync<CollectionResponseDto>(response, "Create collection");
    }

    /// <summary>
    /// Updates an existing collection.
    /// </summary>
    public virtual async Task UpdateCollectionAsync(Guid id, UpdateCollectionDto dto, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync($"/api/collections/{id}", dto, ct);
        await EnsureSuccessAsync(response, "Update collection");
    }

    /// <summary>
    /// Deletes a collection.
    /// </summary>
    public virtual async Task DeleteCollectionAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/collections/{id}", ct);
        await EnsureSuccessAsync(response, "Delete collection");
    }

    /// <summary>
    /// Gets the ACL entries for a collection (manager+).
    /// </summary>
    public virtual async Task<List<CollectionAclResponseDto>> GetCollectionAclsAsync(Guid collectionId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/collections/{collectionId}/acl", ct);
        await EnsureSuccessAsync(response, "Get collection ACLs");
        return await response.Content.ReadFromJsonAsync<List<CollectionAclResponseDto>>(ct) ?? new();
    }

    /// <summary>
    /// Sets (adds or updates) a user's access on a collection (manager+).
    /// </summary>
    public virtual async Task SetCollectionAccessAsync(Guid collectionId, string principalType, string principalId, string role, CancellationToken ct = default)
    {
        var request = new { principalType, principalId, role };
        var response = await _http.PostAsJsonAsync($"/api/collections/{collectionId}/acl", request, ct);
        await EnsureSuccessAsync(response, "Set collection access");
    }

    /// <summary>
    /// Revokes a user's access from a collection (manager+).
    /// </summary>
    public virtual async Task RevokeCollectionAccessAsync(Guid collectionId, string principalType, string principalId, CancellationToken ct = default)
    {
        var url = $"/api/collections/{collectionId}/acl/{Uri.EscapeDataString(principalType)}/{Uri.EscapeDataString(principalId)}";
        var response = await _http.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response, "Revoke collection access");
    }

    /// <summary>
    /// Searches for users that can be added to a collection's ACL (manager+).
    /// Excludes users who already have direct access.
    /// </summary>
    public virtual async Task<List<UserSearchResultDto>> SearchUsersForAclAsync(Guid collectionId, string? query = null, CancellationToken ct = default)
    {
        var url = $"/api/collections/{collectionId}/acl/users/search";
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
        var url = $"/api/assets/collection/{collectionId}?skip={skip}&take={take}&sortBy={sortBy}";
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
    /// Gets all assets across all collections with optional filtering.
    /// </summary>
    public virtual async Task<AllAssetsListResponse> GetAllAssetsAsync(
        string? query = null,
        string? type = null,
        Guid? collectionId = null,
        string sortBy = Constants.SortBy.CreatedDesc,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var url = $"/api/assets/all?skip={skip}&take={take}&sortBy={sortBy}";
        if (!string.IsNullOrWhiteSpace(query))
            url += $"&query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(type))
            url += $"&type={Uri.EscapeDataString(type)}";
        if (collectionId.HasValue)
            url += $"&collectionId={collectionId.Value}";

        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Get all assets");
        return await response.Content.ReadFromJsonAsync<AllAssetsListResponse>(ct)
            ?? new AllAssetsListResponse { Total = 0, Items = new() };
    }

    /// <summary>
    /// Gets a single asset by ID.
    /// </summary>
    /// <returns>The asset, or null if not found.</returns>
    public virtual async Task<AssetResponseDto?> GetAssetAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/assets/{id}", ct);
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
        var response = await _http.PatchAsJsonAsync($"/api/assets/{id}", dto, ct);
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

        var response = await _http.PostAsync("/api/assets", content, ct);
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

        var response = await _http.PostAsJsonAsync("/api/assets/init-upload", request, ct);
        await EnsureSuccessAsync(response, "Init upload");
        return await ReadRequiredJsonAsync<InitUploadResponse>(response, "Init upload");
    }

    /// <summary>
    /// Step 2 of presigned upload: Confirms the file was uploaded to MinIO and triggers processing.
    /// </summary>
    public virtual async Task<AssetUploadResult> ConfirmUploadAsync(Guid assetId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/assets/{assetId}/confirm-upload", null, ct);
        await EnsureSuccessAsync(response, "Confirm upload");
        return await ReadRequiredJsonAsync<AssetUploadResult>(response, "Confirm upload");
    }

    /// <summary>
    /// Deletes an asset. When fromCollectionId is set the asset is removed from that
    /// collection (and auto-deleted by the backend when orphaned). Without fromCollectionId
    /// a full permanent delete is performed.
    /// </summary>
    public virtual async Task DeleteAssetAsync(Guid id, Guid? fromCollectionId = null, CancellationToken ct = default)
    {
        var url = $"/api/assets/{id}";
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
        var response = await _http.PostAsJsonAsync("/api/assets/bulk-delete", request, ct);
        await EnsureSuccessAsync(response, "Bulk delete assets");
        return await ReadRequiredJsonAsync<BulkDeleteAssetsResponse>(response, "Bulk delete assets");
    }

    /// <summary>
    /// Returns deletion context for an asset: how many collections it belongs to
    /// and whether the current user can permanently delete it.
    /// </summary>
    public virtual async Task<AssetDeletionContextDto> GetAssetDeletionContextAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/assets/{id}/deletion-context", ct);
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

        var response = await _http.PostAsJsonAsync("/api/shares", dto, ct);
        await EnsureSuccessAsync(response, "Create share");
        return await ReadRequiredJsonAsync<ShareResponseDto>(response, "Create share");
    }

    /// <summary>
    /// Updates the password for an existing share.
    /// </summary>
    public virtual async Task UpdateSharePasswordAsync(Guid shareId, string newPassword, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/shares/{shareId}/password", new { Password = newPassword }, ct);
        await EnsureSuccessAsync(response, "Update share password");
    }

    /// <summary>
    /// Retrieves the plaintext token for a share if available (admin-only).
    /// Returns empty string when the share predates token encryption.
    /// </summary>
    public virtual async Task<string> GetShareTokenAsync(Guid shareId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/admin/shares/{shareId}/token", ct);
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
        var response = await _http.GetAsync($"/api/admin/shares/{shareId}/password", ct);
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
        var response = await _http.DeleteAsync($"/api/shares/{id}", ct);
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
        var url = $"/api/shares/{Uri.EscapeDataString(token)}";
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
        var url = $"/api/shares/{Uri.EscapeDataString(token)}/access-token";
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
        return Task.FromResult($"/api/assets/{assetId}/download");
    }

    #endregion

    #region Admin

    /// <summary>
    /// Gets all shares in the system (admin only).
    /// </summary>
    public virtual async Task<AdminSharesResponse> GetAllSharesAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/admin/shares?skip={skip}&take={take}", ct);
        await EnsureSuccessAsync(response, "Get all shares");
        return await response.Content.ReadFromJsonAsync<AdminSharesResponse>(ct) ?? new();
    }

    /// <summary>
    /// Revokes any share by ID (admin only).
    /// </summary>
    public virtual async Task RevokeShareAdminAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/admin/shares/{id}", ct);
        await EnsureSuccessAsync(response, "Revoke share");
    }

    /// <summary>
    /// Gets all collections with their ACLs (admin only).
    /// </summary>
    public virtual async Task<List<CollectionAccessDto>> GetCollectionAccessAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/admin/collections/access", ct);
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
        var response = await _http.PostAsJsonAsync($"/api/admin/collections/{collectionId}/acl", request, ct);
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
        var response = await _http.PostAsJsonAsync($"/api/admin/collections/{collectionId}/acl", request, ct);
        await EnsureSuccessAsync(response, "Update collection access");
    }

    /// <summary>
    /// Removes a user or group from a collection's ACL (admin only).
    /// </summary>
    public virtual async Task RemoveCollectionAclAsync(Guid collectionId, string principalId, string principalType, CancellationToken ct = default)
    {
        var url = $"/api/admin/collections/{collectionId}/acl/{Uri.EscapeDataString(principalId)}?principalType={principalType}";
        var response = await _http.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response, "Remove collection access");
    }

    /// <summary>
    /// Bulk-deletes multiple collections (admin only).
    /// </summary>
    public virtual async Task<BulkDeleteCollectionsResponse> BulkDeleteCollectionsAsync(List<Guid> collectionIds, bool deleteAssets = true, CancellationToken ct = default)
    {
        var request = new { collectionIds, deleteAssets };
        var response = await _http.PostAsJsonAsync("/api/admin/collections/bulk-delete", request, ct);
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
        var response = await _http.PostAsJsonAsync("/api/admin/collections/bulk-set-access", request, ct);
        await EnsureSuccessAsync(response, "Bulk set collection access");
        return await ReadRequiredJsonAsync<BulkSetCollectionAccessResponse>(response, "Bulk set collection access");
    }

    /// <summary>
    /// Gets all users who have access to collections (admin only).
    /// </summary>
    public virtual async Task<List<UserAccessSummaryDto>> GetUsersAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/admin/users", ct);
        await EnsureSuccessAsync(response, "Get users");
        return await response.Content.ReadFromJsonAsync<List<UserAccessSummaryDto>>(ct) ?? new();
    }

    /// <summary>
    /// Gets all users from Keycloak realm (admin only).
    /// </summary>
    public virtual async Task<List<KeycloakUserDto>> GetKeycloakUsersAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/admin/keycloak-users", ct);
        await EnsureSuccessAsync(response, "Get Keycloak users");
        return await response.Content.ReadFromJsonAsync<List<KeycloakUserDto>>(ct) ?? new();
    }

    /// <summary>
    /// Creates a new user in Keycloak (admin only).
    /// </summary>
    public virtual async Task<CreateUserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/admin/users", request, ct);
        await EnsureSuccessAsync(response, "Create user");
        return await ReadRequiredJsonAsync<CreateUserResponse>(response, "Create user");
    }

    /// <summary>
    /// Sends a password reset email to a user via Keycloak (admin only).
    /// </summary>
    public virtual async Task SendPasswordResetEmailAsync(string userId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/admin/users/{userId}/reset-password", null, ct);
        await EnsureSuccessAsync(response, "Send password reset email");
    }

    /// <summary>
    /// Deletes a user from Keycloak and cleans up app data (admin only).
    /// </summary>
    public virtual async Task<DeleteUserResponse> DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/admin/users/{userId}", ct);
        await EnsureSuccessAsync(response, "Delete user");
        return await ReadRequiredJsonAsync<DeleteUserResponse>(response, "Delete user");
    }

    /// <summary>
    /// Syncs deleted users — detects and cleans up Keycloak users that no longer exist (admin only).
    /// </summary>
    public virtual async Task<UserSyncResult> SyncDeletedUsersAsync(bool dryRun = false, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/admin/users/sync?dryRun={dryRun.ToString().ToLowerInvariant()}", null, ct);
        await EnsureSuccessAsync(response, "Sync deleted users");
        return await ReadRequiredJsonAsync<UserSyncResult>(response, "Sync deleted users");
    }

    /// <summary>
    /// Gets all audit events (admin only).
    /// </summary>
    public virtual async Task<List<AuditEventDto>> GetAuditEventsAsync(int take = 200, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/admin/audit?take={take}", ct);
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

        var url = $"/api/admin/audit/paginated?{string.Join("&", queryParams)}";
        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Get audit events paginated");
        return await response.Content.ReadFromJsonAsync<AuditQueryResponse>(ct) ?? new();
    }

    #endregion

    #region Asset Collections (Multi-Collection)

    /// <summary>
    /// Gets all collections an asset belongs to.
    /// </summary>
    public virtual async Task<List<AssetCollectionDto>> GetAssetCollectionsAsync(Guid assetId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/assets/{assetId}/collections", ct);
        await EnsureSuccessAsync(response, "Get asset collections");
        return await response.Content.ReadFromJsonAsync<List<AssetCollectionDto>>(ct) ?? new();
    }

    /// <summary>
    /// Adds an asset to a collection.
    /// </summary>
    public virtual async Task AddAssetToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/assets/{assetId}/collections/{collectionId}", null, ct);
        await EnsureSuccessAsync(response, "Add asset to collection");
    }

    /// <summary>
    /// Removes an asset from a collection.
    /// </summary>
    public virtual async Task RemoveAssetFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/assets/{assetId}/collections/{collectionId}", ct);
        await EnsureSuccessAsync(response, "Remove asset from collection");
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
