namespace AssetHub.Application.Configuration;

/// <summary>
/// Settings for the ASP.NET Core Data Protection keyring. The keyring is
/// persisted to the database (so all instances share keys) and, in
/// production, wrapped with an X.509 certificate so that an attacker who
/// only exfiltrates the database cannot decrypt the keys.
/// </summary>
public class DataProtectionSettings
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Path to a PFX (PKCS#12) certificate file used to encrypt the
    /// keyring at rest. Mount via Docker secret in production
    /// (<c>/run/secrets/assethub-dp-cert.pfx</c> or similar) and supply
    /// <see cref="CertificatePassword"/> from a separate secret.
    /// </summary>
    /// <remarks>
    /// When unset in non-Development environments,
    /// <c>AddAssetHubServices</c> throws at startup. Without certificate
    /// wrapping the keyring sits in plaintext alongside the data it
    /// protects (share tokens, share passwords, webhook secrets,
    /// migration secrets, signed magic-links, signed unsubscribe tokens)
    /// — a single DB exfil yields all of them. (A-1/A-2 in the
    /// security review.)
    /// </remarks>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Password for the PFX file at <see cref="CertificatePath"/>. Mount
    /// via a separate Docker secret. Empty string means "no password"
    /// (only acceptable for dev-only test certs).
    /// </summary>
    public string? CertificatePassword { get; set; }
}
