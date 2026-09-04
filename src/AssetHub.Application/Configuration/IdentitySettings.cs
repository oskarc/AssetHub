using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Local ASP.NET Core Identity configuration. Bound to the "Identity" section.
/// </summary>
public class IdentitySettings
{
    public const string SectionName = "Identity";

    /// <summary>Minimum password length. Below 8 is rejected at startup.</summary>
    [Range(8, 128)]
    public int PasswordMinLength { get; set; } = 12;

    /// <summary>Require at least one non-alphanumeric character.</summary>
    public bool PasswordRequireNonAlphanumeric { get; set; } = true;

    /// <summary>Require at least one digit.</summary>
    public bool PasswordRequireDigit { get; set; } = true;

    /// <summary>Require both an upper- and a lower-case character.</summary>
    public bool PasswordRequireMixedCase { get; set; } = true;

    /// <summary>Failed sign-in attempts before the account locks out.</summary>
    [Range(1, 20)]
    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>Lockout duration in minutes once the attempt limit is hit.</summary>
    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Whether to create roles and the bootstrap admin at startup. Production
    /// leaves this on — an Identity deployment with no administrator cannot be
    /// signed into. Test hosts that provision their own schema turn it off.
    /// </summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>
    /// Admin account created on first start when the Identity provider is
    /// selected and the user store is empty. Seeding is skipped entirely when
    /// any user already exists, so this never overwrites a real account.
    /// </summary>
    public SeedAdminSettings SeedAdmin { get; set; } = new();

    /// <summary>
    /// Seed a fixed-credential test viewer alongside the admin on first start.
    /// DEFAULTS OFF and must be turned on explicitly (dev/CI compose only) — a
    /// known-password account must never exist in production. Like the admin, it
    /// is created only when the user store is empty.
    /// </summary>
    public bool SeedTestViewer { get; set; }

    /// <summary>Credentials for the optional test viewer (see <see cref="SeedTestViewer"/>).</summary>
    public SeedViewerSettings TestViewer { get; set; } = new();
}

/// <summary>
/// A fixed-credential viewer for the E2E gate, so a fresh database is testable
/// with no hand-provisioning. Only ever seeded when <see cref="IdentitySettings.SeedTestViewer"/>
/// is true, which is dev/CI-only.
/// </summary>
public class SeedViewerSettings
{
    /// <summary>Username for the seeded test viewer.</summary>
    public string UserName { get; set; } = "testuser";

    /// <summary>Email for the seeded test viewer.</summary>
    [EmailAddress]
    public string Email { get; set; } = "test@example.com";

    /// <summary>Password for the seeded test viewer. Dev/CI only; supplied from config.</summary>
    public string Password { get; set; } = "";
}

/// <summary>
/// The bootstrap administrator. Without it, an Identity-backed deployment has
/// no way to sign in for the first time.
/// </summary>
public class SeedAdminSettings
{
    /// <summary>Username for the seeded admin.</summary>
    [Required]
    public string UserName { get; set; } = "admin";

    /// <summary>Email for the seeded admin.</summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    /// <summary>
    /// Initial password. Supplied from environment or Docker secret — never a
    /// literal in appsettings. Seeding fails loudly rather than inventing a
    /// default, so a deployment cannot silently ship a known password.
    /// </summary>
    [Required]
    public string Password { get; set; } = "";
}
