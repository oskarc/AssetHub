using System.Security.Claims;
using Dam.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AssetHub.Extensions;

/// <summary>
/// Configures Keycloak-based authentication (OIDC + JWT + Cookie) and
/// role-based authorization policies.
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddAssetHubAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var keycloakConfig = configuration.GetSection("Keycloak");
        var keycloakAuthority = keycloakConfig["Authority"]
            ?? throw new InvalidOperationException("Keycloak:Authority is required.");
        var clientSecret = keycloakConfig["ClientSecret"]
            ?? throw new InvalidOperationException("Keycloak:ClientSecret is required.");
        var requireHttpsMetadata = keycloakConfig.GetValue("RequireHttpsMetadata", true);

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Smart";
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddPolicyScheme("Smart", "Smart Auth Selector", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                string? authorization = context.Request.Headers.Authorization;
                if (!string.IsNullOrEmpty(authorization) &&
                    authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                return CookieAuthenticationDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(options =>
        {
            options.Authority = keycloakAuthority;
            options.RequireHttpsMetadata = requireHttpsMetadata;
            if (environment.IsDevelopment())
            {
                options.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
            }
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = keycloakAuthority,
                ValidateAudience = true,
                ValidAudiences = new[] { "assethub-app", "account" },
                ValidateLifetime = true,
                NameClaimType = "preferred_username",
                RoleClaimType = ClaimTypes.Role
            };
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = "__Host.assethub.auth";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        })
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            ConfigureOpenIdConnect(options, keycloakConfig, keycloakAuthority, clientSecret,
                requireHttpsMetadata, environment);
        });

        // ── Authorization policies ──────────────────────────────────────────
        services.AddAuthorization(options =>
        {
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

        return services;
    }

    // ── OpenID Connect configuration ────────────────────────────────────────

    private static void ConfigureOpenIdConnect(
        OpenIdConnectOptions options,
        IConfigurationSection keycloakConfig,
        string keycloakAuthority,
        string clientSecret,
        bool requireHttpsMetadata,
        IWebHostEnvironment environment)
    {
        options.Authority = keycloakAuthority;
        options.MetadataAddress = keycloakAuthority + "/.well-known/openid-configuration";

        var clientId = keycloakConfig["ClientId"]
            ?? throw new InvalidOperationException("Keycloak:ClientId is required.");
        options.ClientId = clientId;
        options.ClientSecret = clientSecret;

        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (environment.IsDevelopment())
        {
            options.BackchannelHttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
        }

        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
        options.SkipUnrecognizedRequests = true;
        options.UsePkce = true;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role,
            ValidIssuer = keycloakAuthority
        };

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                if (context.Properties.Items.TryGetValue("kc_action", out var kcAction))
                {
                    context.ProtocolMessage.SetParameter("kc_action", kcAction);
                }
                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(context.Failure, "OIDC remote failure: {Message}", context.Failure?.Message);
                context.HandleResponse();
                context.Response.Redirect("/?authError=oidc_remote_failure");
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError("OIDC authentication failed: {Message}", context.Exception?.Message ?? "Unknown");
                context.HandleResponse();
                context.Response.Redirect("/?authError=authentication_failed");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity)
                    return Task.CompletedTask;

                MapKeycloakRoles(identity);
                return Task.CompletedTask;
            }
        };
    }

    // ── Keycloak role mapping ───────────────────────────────────────────────

    private static void MapKeycloakRoles(ClaimsIdentity identity)
    {
        // Realm roles from "realm_access" claim
        var realmAccess = identity.FindFirst("realm_access")?.Value;
        if (!string.IsNullOrWhiteSpace(realmAccess))
        {
            foreach (var role in ExtractRolesFromJson(realmAccess))
                AddRoleIfMissing(identity, role);
        }

        // Client roles from "resource_access" claim
        var resourceAccess = identity.FindFirst("resource_access")?.Value;
        if (!string.IsNullOrWhiteSpace(resourceAccess))
        {
            foreach (var role in ExtractClientRolesFromJson(resourceAccess, "assethub-app"))
                AddRoleIfMissing(identity, role);
        }
    }

    private static void AddRoleIfMissing(ClaimsIdentity identity, string role)
    {
        if (!identity.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == role))
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
    }

    // ── JSON helpers (Keycloak token parsing) ───────────────────────────────

    internal static IEnumerable<string> ExtractRolesFromJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("roles", out var rolesProp) ||
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
