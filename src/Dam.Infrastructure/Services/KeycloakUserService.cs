using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dam.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

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

    // Token cache
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public KeycloakUserService(
        IConfiguration configuration,
        ILogger<KeycloakUserService> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;

        // Parse authority URL to extract base URL and realm
        // Authority is like "http://keycloak:8080/realms/media"
        var authority = configuration["Keycloak:Authority"] 
            ?? throw new InvalidOperationException("Keycloak:Authority is required");
        
        var authorityUri = new Uri(authority);
        _keycloakBaseUrl = $"{authorityUri.Scheme}://{authorityUri.Authority}";
        
        // Extract realm name from path (e.g., "/realms/media" -> "media")
        var pathSegments = authorityUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        _realm = pathSegments.Length >= 2 ? pathSegments[1] : "master";

        // Admin credentials for Keycloak Admin API
        _adminUsername = configuration["Keycloak:AdminUsername"] ?? "admin";
        _adminPassword = configuration["Keycloak:AdminPassword"] 
            ?? throw new InvalidOperationException("Keycloak:AdminPassword is required for user management");
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

    /// <summary>
    /// Obtains an admin access token from Keycloak's master realm using resource owner password credentials.
    /// Caches the token until it expires.
    /// </summary>
    private async Task<string> GetAdminTokenAsync(CancellationToken ct)
    {
        // Return cached token if still valid (with 30s buffer)
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddSeconds(-30))
            return _cachedToken;

        var tokenUrl = $"{_keycloakBaseUrl}/realms/master/protocol/openid-connect/token";

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = _adminUsername,
            ["password"] = _adminPassword
        };

        using var response = await _httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(formData), ct);
        
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
        catch
        {
            // Not valid JSON
        }
        return null;
    }
}
