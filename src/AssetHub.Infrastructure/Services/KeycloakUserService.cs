using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable InconsistentNaming

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Keycloak Admin REST API implementation for user management.
/// Uses client credentials (admin username/password) to obtain an admin access token,
/// then calls the Keycloak Admin API to create users.
/// </summary>
public class KeycloakUserService : IKeycloakUserService
{
    private readonly ILogger<KeycloakUserService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _keycloakBaseUrl;
    private readonly string _realm;
    private readonly string _adminUsername;
    private readonly string _adminPassword;
    private readonly string _adminClientId;
    private readonly string? _adminClientSecret;
    private readonly bool _useClientCredentials;

    // Token cache — guarded by _tokenLock for thread safety
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public KeycloakUserService(
        IOptions<KeycloakSettings> keycloakSettings,
        ILogger<KeycloakUserService> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;

        var settings = keycloakSettings.Value;

        // Parse authority URL to extract base URL and realm
        // Authority is like "http://keycloak:8080/realms/media"
        if (string.IsNullOrWhiteSpace(settings.Authority))
            throw new InvalidOperationException("Keycloak:Authority is required. Check appsettings for the current environment.");
        
        var authorityUri = new Uri(settings.Authority);
        _keycloakBaseUrl = $"{authorityUri.Scheme}://{authorityUri.Authority}";
        
        // Extract realm name from path (e.g., "/realms/media" -> "media")
        var pathSegments = authorityUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        _realm = pathSegments.Length >= 2 ? pathSegments[1] : "master";

        // Admin credentials for Keycloak Admin API
        if (string.IsNullOrWhiteSpace(settings.AdminUsername))
            throw new InvalidOperationException("Keycloak:AdminUsername is required. Check appsettings for the current environment.");
        _adminUsername = settings.AdminUsername;
        if (string.IsNullOrWhiteSpace(settings.AdminPassword))
            throw new InvalidOperationException("Keycloak:AdminPassword is required. Check appsettings for the current environment.");
        _adminPassword = settings.AdminPassword;

        // Service account settings for client_credentials grant (preferred)
        _adminClientId = settings.AdminClientId;
        _adminClientSecret = settings.AdminClientSecret;
        _useClientCredentials = !string.IsNullOrWhiteSpace(_adminClientSecret);

        if (_useClientCredentials)
            _logger.LogInformation("Using client_credentials grant for Keycloak admin API (client: {ClientId})", _adminClientId);
        else
            _logger.LogInformation("Using password grant for Keycloak admin API (user: {Username})", _adminUsername);
    }

    /// <inheritdoc />
    public async Task<string> CreateUserAsync(
        string username,
        string email,
        string firstName,
        string lastName,
        string password,
        bool temporaryPassword = true,
        CancellationToken ct = default)
    {
        try
        {
            var token = await GetAdminTokenAsync(ct);

            var userPayload = new
            {
                username,
                email,
                firstName,
                lastName,
                enabled = true,
                emailVerified = false,
                credentials = new[]
                {
                    new
                    {
                        type = "password",
                        value = password,
                        temporary = temporaryPassword
                    }
                }
            };

            var url = $"{_keycloakBaseUrl}/admin/realms/{_realm}/users";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(userPayload);

            using var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Keycloak user creation conflict for '{Username}': {Error}", username, errorBody);
                
                // Try to extract a meaningful error message
                var message = ExtractKeycloakErrorMessage(errorBody) 
                    ?? "A user with this username or email already exists";
                throw new KeycloakApiException(message, (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Keycloak user creation failed for '{Username}'. Status: {Status}, Body: {Body}",
                    username, response.StatusCode, errorBody);
            
            var message = ExtractKeycloakErrorMessage(errorBody) 
                ?? $"Failed to create user (HTTP {(int)response.StatusCode})";
            throw new KeycloakApiException(message, (int)response.StatusCode);
        }

        // Extract user ID from Location header
        // Location: http://keycloak:8080/admin/realms/media/users/{userId}
        var location = response.Headers.Location?.ToString();
        if (!string.IsNullOrEmpty(location))
        {
            var userId = location.Split('/').Last();
            _logger.LogInformation("Successfully created Keycloak user '{Username}' with ID '{UserId}'", username, userId);
            return userId;
        }

        // Fallback: query for the user by username to get the ID
        _logger.LogWarning("No Location header in create user response, looking up user by username");
        var lookupUrl = $"{_keycloakBaseUrl}/admin/realms/{_realm}/users?username={Uri.EscapeDataString(username)}&exact=true";
        
        using var lookupRequest = new HttpRequestMessage(HttpMethod.Get, lookupUrl);
        lookupRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        
        using var lookupResponse = await _httpClient.SendAsync(lookupRequest, ct);
        if (lookupResponse.IsSuccessStatusCode)
        {
            var users = await lookupResponse.Content.ReadFromJsonAsync<JsonElement[]>(ct);
            if (users is { Length: > 0 })
            {
                var userId = users[0].GetProperty("id").GetString()!;
                _logger.LogInformation("Resolved Keycloak user '{Username}' to ID '{UserId}' via lookup", username, userId);
                return userId;
            }
        }

        throw new KeycloakApiException("User was created but could not determine the user ID");
        }
        catch (KeycloakApiException)
        {
            throw; // Already a Keycloak exception, rethrow as-is
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while creating Keycloak user '{Username}'", username);
            throw new KeycloakApiException("Authentication service is temporarily unavailable. Please try again.", 0, ex);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Connection error while creating Keycloak user '{Username}'", username);
            throw new KeycloakApiException("Authentication service is temporarily unavailable. Please try again.", 0, ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout while creating Keycloak user '{Username}'", username);
            throw new KeycloakApiException("Authentication service request timed out. Please try again.", 0, ex);
        }
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(
        string userId,
        string newPassword,
        bool temporary = true,
        CancellationToken ct = default)
    {
        try
        {
            var token = await GetAdminTokenAsync(ct);

            var credentialPayload = new
            {
                type = "password",
                value = newPassword,
                temporary
            };

            var url = $"{_keycloakBaseUrl}/admin/realms/{_realm}/users/{Uri.EscapeDataString(userId)}/reset-password";

            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(credentialPayload);

            using var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new KeycloakApiException("User not found in Keycloak", (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Keycloak password reset failed for user '{UserId}'. Status: {Status}, Body: {Body}",
                    userId, response.StatusCode, errorBody);

                var message = ExtractKeycloakErrorMessage(errorBody)
                    ?? $"Failed to reset password (HTTP {(int)response.StatusCode})";
                throw new KeycloakApiException(message, (int)response.StatusCode);
            }

            _logger.LogInformation("Successfully reset password for Keycloak user '{UserId}'", userId);
        }
        catch (KeycloakApiException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while resetting password for Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service is temporarily unavailable. Please try again.", 0, ex);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Connection error while resetting password for Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service is temporarily unavailable. Please try again.", 0, ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout while resetting password for Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service request timed out. Please try again.", 0, ex);
        }
    }

    /// <inheritdoc />
    public async Task SendExecuteActionsEmailAsync(
        string userId,
        IEnumerable<string> actions,
        int? lifespan = null,
        CancellationToken ct = default)
    {
        try
        {
            var token = await GetAdminTokenAsync(ct);

            var url = $"{_keycloakBaseUrl}/admin/realms/{_realm}/users/{Uri.EscapeDataString(userId)}/execute-actions-email";
            if (lifespan.HasValue)
                url += $"?lifespan={lifespan.Value}";

            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(actions);

            using var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new KeycloakApiException("User not found in Keycloak", (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Keycloak execute-actions-email failed for user '{UserId}'. Status: {Status}, Body: {Body}",
                    userId, response.StatusCode, errorBody);

                var message = ExtractKeycloakErrorMessage(errorBody)
                    ?? $"Failed to send actions email (HTTP {(int)response.StatusCode})";
                throw new KeycloakApiException(message, (int)response.StatusCode);
            }

            _logger.LogInformation(
                "Sent execute-actions-email to Keycloak user '{UserId}' with actions: {Actions}",
                userId, string.Join(", ", actions));
        }
        catch (KeycloakApiException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while sending actions email for Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service is temporarily unavailable. Please try again.", 0, ex);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Connection error while sending actions email for Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service is temporarily unavailable. Please try again.", 0, ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout while sending actions email for Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service request timed out. Please try again.", 0, ex);
        }
    }

    /// <inheritdoc />
    public async Task DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var token = await GetAdminTokenAsync(ct);

            var url = $"{_keycloakBaseUrl}/admin/realms/{_realm}/users/{Uri.EscapeDataString(userId)}";

            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new KeycloakApiException("User not found in Keycloak", (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Keycloak user deletion failed for '{UserId}'. Status: {Status}, Body: {Body}",
                    userId, response.StatusCode, errorBody);

            var message = ExtractKeycloakErrorMessage(errorBody)
                ?? $"Failed to delete user (HTTP {(int)response.StatusCode})";
            throw new KeycloakApiException(message, (int)response.StatusCode);
            }

            _logger.LogInformation("Successfully deleted Keycloak user '{UserId}'", userId);
        }
        catch (KeycloakApiException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while deleting Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service is temporarily unavailable. Please try again.", 0, ex);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Connection error while deleting Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service is temporarily unavailable. Please try again.", 0, ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout while deleting Keycloak user '{UserId}'", userId);
            throw new KeycloakApiException("Authentication service request timed out. Please try again.", 0, ex);
        }
    }

    /// <summary>
    /// Obtains an admin access token from Keycloak's master realm.
    /// Uses client_credentials grant if AdminClientSecret is configured, otherwise falls back to password grant.
    /// Caches the token until it expires.
    /// </summary>
    private async Task<string> GetAdminTokenAsync(CancellationToken ct)
    {
        // Fast path: return cached token if still valid (with 30s buffer)
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddSeconds(-30))
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock — another thread may have refreshed
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddSeconds(-30))
                return _cachedToken;

            var tokenUrl = $"{_keycloakBaseUrl}/realms/master/protocol/openid-connect/token";

            Dictionary<string, string> formData;

            if (_useClientCredentials)
            {
                // Preferred: client_credentials grant with service account
                formData = new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _adminClientId,
                    ["client_secret"] = _adminClientSecret!
                };
            }
            else
            {
                // Fallback: password grant (ROPC) - less secure but simpler setup
                formData = new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = _adminClientId,
                    ["username"] = _adminUsername,
                    ["password"] = _adminPassword
                };
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(formData), ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error while obtaining Keycloak admin token");
                throw new KeycloakApiException("Authentication service is temporarily unavailable.", 0, ex);
            }
            catch (SocketException ex)
            {
                _logger.LogError(ex, "Connection error while obtaining Keycloak admin token");
                throw new KeycloakApiException("Authentication service is temporarily unavailable.", 0, ex);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError(ex, "Timeout while obtaining Keycloak admin token");
                throw new KeycloakApiException("Authentication service request timed out.", 0, ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("Failed to obtain Keycloak admin token. Status: {Status}, Body: {Body}",
                        response.StatusCode, errorBody);
                    throw new KeycloakApiException(
                        "Failed to authenticate with Keycloak Admin API. Check admin credentials.",
                        (int)response.StatusCode);
                }

                var tokenResponse = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

                _cachedToken = tokenResponse.GetProperty("access_token").GetString()!;
                var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
                _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

                _logger.LogDebug("Obtained Keycloak admin token, expires in {ExpiresIn}s", expiresIn);

                return _cachedToken;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Extracts a user-friendly error message from Keycloak's JSON error response.
    /// </summary>
    private static string? ExtractKeycloakErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("errorMessage", out var errorMessage))
                return errorMessage.GetString();
            if (doc.RootElement.TryGetProperty("error_description", out var errorDesc))
                return errorDesc.GetString();
            if (doc.RootElement.TryGetProperty("error", out var error))
                return error.GetString();
        }
        catch (JsonException)
        {
            // Not valid JSON — return null to fall back to a generic message
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<HashSet<string>> GetRealmRoleMemberIdsAsync(string roleName, CancellationToken ct = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            var token = await GetAdminTokenAsync(ct);
            var pageSize = 100;
            var first = 0;

            while (true)
            {
                var url = $"{_keycloakBaseUrl}/admin/realms/{_realm}/roles/{Uri.EscapeDataString(roleName)}/users?first={first}&max={pageSize}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                using var response = await _httpClient.SendAsync(request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get realm role members for '{Role}'. Status: {Status}", roleName, response.StatusCode);
                    return ids;
                }

                var users = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

                if (users.ValueKind != JsonValueKind.Array || users.GetArrayLength() == 0)
                    break;

                foreach (var user in users.EnumerateArray())
                {
                    if (user.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id)
                        ids.Add(id);
                }

                if (users.GetArrayLength() < pageSize)
                    break;

                first += pageSize;
            }
        }
        catch (KeycloakApiException ex)
        {
            _logger.LogWarning(ex, "Failed to get realm role members for '{Role}'", roleName);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error while getting realm role members for '{Role}'", roleName);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "Connection error while getting realm role members for '{Role}'", roleName);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Timeout while getting realm role members for '{Role}'", roleName);
        }

        return ids;
    }
}
