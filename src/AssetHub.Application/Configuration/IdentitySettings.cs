using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Local ASP.NET Core Identity configuration. Bound to the "Identity" section.
/// Only consulted when <see cref="AuthSettings.Provider"/> is <c>Identity</c>.
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
    /// Admin account created on first start when the Identity provider is
    /// selected and the user store is empty. Seeding is skipped entirely when
    /// any user already exists, so this never overwrites a real account.
    /// </summary>
    public SeedAdminSettings SeedAdmin { get; set; } = new();
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
