using AssetHub.Application.Configuration;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using AssetHub.Application;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AssetHub.Api.Extensions;

/// <summary>
/// Configures local Identity authentication (cookie) and
/// role-based authorization policies.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Auth wiring touches OIDC + JWT + Cookie + scheme selector + every authorization policy.")]
public static class AuthenticationExtensions
{
    /// <summary>
    /// Wires local ASP.NET Core Identity authentication and the shared
    /// authorization policies.
    /// </summary>
    public static IServiceCollection AddAssetHubAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        AddIdentityProvider(services, environment);

        ConfigureAuthorizationPolicies(services);

        return services;
    }

    /// <summary>
    /// Local Identity: cookie sign-in against the app's own user store. JWT
    /// bearer stays wired so service callers and the Smart selector behave the
    /// </summary>
    private static void AddIdentityProvider(IServiceCollection services, IWebHostEnvironment environment)
    {
        // AddIdentityCookies() is what actually registers the Identity.Application
        // cookie handler. AddIdentityCore + AddSignInManager does NOT — without
        // this, SignInAsync throws "No sign-in authentication handlers are
        // registered" and ConfigureApplicationCookie below silently configures a
        // scheme that does not exist.
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        })
        .AddIdentityCookies();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AssetHubDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Password and lockout policy come from IdentitySettings so a deployment
        // can tighten them without a code change.
        services.AddOptions<IdentityOptions>()
            .Configure<IOptions<IdentitySettings>>((identity, cfg) =>
            {
                var s = cfg.Value;
                identity.Password.RequiredLength = s.PasswordMinLength;
                identity.Password.RequireNonAlphanumeric = s.PasswordRequireNonAlphanumeric;
                identity.Password.RequireDigit = s.PasswordRequireDigit;
                identity.Password.RequireUppercase = s.PasswordRequireMixedCase;
                identity.Password.RequireLowercase = s.PasswordRequireMixedCase;
                identity.Lockout.MaxFailedAccessAttempts = s.MaxFailedAccessAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(s.LockoutMinutes);
                identity.Lockout.AllowedForNewUsers = true;
            });

        services.AddScoped<IdentitySeeder>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = environment.IsDevelopment()
                ? "assethub.auth"
                : "__Host-assethub.auth";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.LoginPath = "/login";
            options.LogoutPath = "/auth/logout";
            options.AccessDeniedPath = "/login";
        });
    }

    private static void ConfigureAuthorizationPolicies(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy("Authenticated", policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy("RequireViewer", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole(RoleHierarchy.Roles.Viewer) ||
                    context.User.IsInRole(RoleHierarchy.Roles.Contributor) ||
                    context.User.IsInRole(RoleHierarchy.Roles.Manager) ||
                    context.User.IsInRole(RoleHierarchy.Roles.Admin)));

            options.AddPolicy("RequireContributor", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole(RoleHierarchy.Roles.Contributor) ||
                    context.User.IsInRole(RoleHierarchy.Roles.Manager) ||
                    context.User.IsInRole(RoleHierarchy.Roles.Admin)));

            options.AddPolicy("RequireManager", policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole(RoleHierarchy.Roles.Manager) ||
                    context.User.IsInRole(RoleHierarchy.Roles.Admin)));

            options.AddPolicy("RequireAdmin", policy =>
                policy.RequireRole(RoleHierarchy.Roles.Admin));
        });
    }

    internal static IEnumerable<string> ExtractClientRolesFromJson(string json, string clientId)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(clientId, out var clientObj) ||
                clientObj.ValueKind != System.Text.Json.JsonValueKind.Object)
                return Array.Empty<string>();

            if (!clientObj.TryGetProperty("roles", out var rolesProp) ||
                rolesProp.ValueKind != System.Text.Json.JsonValueKind.Array)
                return Array.Empty<string>();

            return rolesProp.EnumerateArray()
                .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
