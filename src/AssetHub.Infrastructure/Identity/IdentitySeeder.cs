using AssetHub.Application;
using AssetHub.Application.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Identity;

/// <summary>
/// Creates the four application roles and, on a completely empty user store, one
/// bootstrap administrator. Without this an Identity-backed deployment has no
/// way to sign in for the first time.
/// </summary>
/// <remarks>
/// Idempotent and conservative: roles are created only when missing, and the
/// admin is seeded ONLY when the store holds no users at all. That means the
/// seeder can never overwrite, re-enable, or reset a real account — once a
/// deployment has any user, seeding is permanently a no-op.
/// </remarks>
public sealed class IdentitySeeder(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentitySettings> settings,
    ILogger<IdentitySeeder> logger)
{
    private static readonly string[] AllRoles =
    [
        RoleHierarchy.Roles.Viewer,
        RoleHierarchy.Roles.Contributor,
        RoleHierarchy.Roles.Manager,
        RoleHierarchy.Roles.Admin
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var role in AllRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var created = await roleManager.CreateAsync(new IdentityRole(role));
                if (!created.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to create role '{role}': {Describe(created)}");
                logger.LogInformation("Created Identity role {Role}", role);
            }
        }

        if (userManager.Users.Any())
        {
            logger.LogDebug("Identity user store is not empty — skipping admin seed");
            return;
        }

        var seed = settings.Value.SeedAdmin;
        if (string.IsNullOrWhiteSpace(seed.Password) || string.IsNullOrWhiteSpace(seed.Email))
        {
            // Fail loudly. Inventing a default password here would ship a known
            // credential to every deployment that forgot to configure one.
            throw new InvalidOperationException(
                "Auth:Provider is 'Identity' and the user store is empty, but Identity:SeedAdmin " +
                "Email/Password are not configured. Supply them via environment or Docker secrets.");
        }

        var admin = new AppUser
        {
            UserName = seed.UserName,
            Email = seed.Email,
            EmailConfirmed = true,
            DisplayName = seed.UserName
        };

        var result = await userManager.CreateAsync(admin, seed.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to seed admin user: {Describe(result)}");

        var roleResult = await userManager.AddToRoleAsync(admin, RoleHierarchy.Roles.Admin);
        if (!roleResult.Succeeded)
            throw new InvalidOperationException($"Failed to grant admin role: {Describe(roleResult)}");

        logger.LogWarning(
            "Seeded bootstrap administrator {UserName}. Change this password immediately.",
            seed.UserName);

        await SeedTestViewerAsync();
    }

    /// <summary>
    /// Seeds a fixed-credential test viewer for the E2E gate, but ONLY when
    /// <see cref="IdentitySettings.SeedTestViewer"/> is explicitly on (dev/CI). It
    /// runs inside the empty-store path above, so like the admin it never touches an
    /// existing deployment. The gate defaults off, so production can never create a
    /// known-password account even by accident.
    /// </summary>
    private async Task SeedTestViewerAsync()
    {
        var opts = settings.Value;
        if (!opts.SeedTestViewer)
            return;

        var viewer = opts.TestViewer;
        if (string.IsNullOrWhiteSpace(viewer.Password) || string.IsNullOrWhiteSpace(viewer.Email))
        {
            logger.LogWarning(
                "Identity:SeedTestViewer is on but TestViewer Email/Password are not configured — skipping.");
            return;
        }

        var user = new AppUser
        {
            UserName = viewer.UserName,
            Email = viewer.Email,
            EmailConfirmed = true,
            DisplayName = viewer.UserName
        };

        var created = await userManager.CreateAsync(user, viewer.Password);
        if (!created.Succeeded)
            throw new InvalidOperationException($"Failed to seed test viewer: {Describe(created)}");

        var roleResult = await userManager.AddToRoleAsync(user, RoleHierarchy.Roles.Viewer);
        if (!roleResult.Succeeded)
            throw new InvalidOperationException($"Failed to grant viewer role: {Describe(roleResult)}");

        logger.LogWarning(
            "Seeded fixed-credential TEST viewer {UserName} (Identity:SeedTestViewer is on — dev/CI only).",
            viewer.UserName);
    }

    private static string Describe(IdentityResult result)
        => string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}
