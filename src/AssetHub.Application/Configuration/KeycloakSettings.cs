using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

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
    [Required]
    public string Authority { get; set; } = "";

    /// <summary>
    /// The OIDC client ID registered in Keycloak.
    /// </summary>
    [Required]
    public string ClientId { get; set; } = "";

    /// <summary>
    /// The OIDC client secret.
    /// </summary>
    [Required]
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Admin username for Keycloak Admin REST API.
    /// </summary>
    [Required]
    public string AdminUsername { get; set; } = "";

    /// <summary>
    /// Admin password for Keycloak Admin REST API.
    /// </summary>
    [Required]
    public string AdminPassword { get; set; } = "";

    /// <summary>
    /// Client ID used for admin token requests (default: "admin-cli").
    /// </summary>
    public string AdminClientId { get; set; } = "admin-cli";

    /// <summary>
    /// Client secret for admin token requests (optional).
    /// When set, client_credentials grant is used instead of password grant.
    /// </summary>
    public string? AdminClientSecret { get; set; }

    /// <summary>
    /// Whether HTTPS metadata is required for OIDC discovery.
    /// Must be true in production.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Explicit opt-in to allow falling back to the OAuth 2 password grant
    /// for the admin token (when <see cref="AdminClientSecret"/> is empty).
    /// Password grant is OAuth 2.1-deprecated, bypasses MFA, and exposes a
    /// real human credential to the admin path. Required to be set true in
    /// non-Development environments if password grant is the only option.
    /// </summary>
    /// <remarks>
    /// Default <c>false</c>. Production setups should configure
    /// <see cref="AdminClientSecret"/> for client_credentials grant; this
    /// flag is an emergency escape hatch for environments where service
    /// accounts aren't available yet.
    /// </remarks>
    public bool AllowAdminPasswordGrant { get; set; }

    /// <summary>
    /// Timeout in seconds for Keycloak HTTP requests.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
