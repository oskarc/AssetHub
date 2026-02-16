namespace Dam.Application.Configuration;

/// <summary>
/// Configuration settings for Keycloak authentication and admin API.
/// Bound to the "Keycloak" section in appsettings.
/// </summary>
public class KeycloakSettings
{
    public const string SectionName = "Keycloak";

    /// <summary>
    /// The Keycloak realm authority URL (e.g. "https://keycloak.example.com/realms/media").
    /// </summary>
    public string Authority { get; set; } = "";

    /// <summary>
    /// The OIDC client ID registered in Keycloak.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// The OIDC client secret.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Admin username for Keycloak Admin REST API.
    /// </summary>
    public string AdminUsername { get; set; } = "";

    /// <summary>
    /// Admin password for Keycloak Admin REST API.
    /// </summary>
    public string AdminPassword { get; set; } = "";

    /// <summary>
    /// Client ID used for admin token requests (default: "admin-cli").
    /// </summary>
    public string AdminClientId { get; set; } = "admin-cli";

    /// <summary>
    /// Whether HTTPS metadata is required for OIDC discovery.
    /// Must be true in production.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Timeout in seconds for Keycloak HTTP requests.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
