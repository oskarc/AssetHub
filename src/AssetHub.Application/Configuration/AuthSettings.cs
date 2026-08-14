using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Which identity provider backs authentication. Bound to the "Auth" section.
/// </summary>
/// <remarks>
/// The 2026-08 reshape is moving AssetHub off Keycloak and onto ASP.NET Core
/// Identity. Both providers are wired simultaneously so the move can be proven
/// before the old one is removed; <see cref="Provider"/> selects between them
/// and defaults to Keycloak, so an existing deployment behaves exactly as before
/// until it opts in.
/// </remarks>
public class AuthSettings
{
    public const string SectionName = "Auth";

    /// <summary>Keycloak-backed OIDC (the default).</summary>
    public const string ProviderKeycloak = "Keycloak";

    /// <summary>Local ASP.NET Core Identity with username/password sign-in.</summary>
    public const string ProviderIdentity = "Identity";

    /// <summary>
    /// Selected provider: <c>Keycloak</c> or <c>Identity</c>. Case-insensitive.
    /// </summary>
    [Required]
    public string Provider { get; set; } = ProviderKeycloak;

    /// <summary>True when the local Identity provider is selected.</summary>
    public bool UsesIdentity =>
        string.Equals(Provider, ProviderIdentity, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the Keycloak provider is selected.</summary>
    public bool UsesKeycloak =>
        string.Equals(Provider, ProviderKeycloak, StringComparison.OrdinalIgnoreCase);
}
