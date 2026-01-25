using System.Security.Claims;
using AssetHub.Components;
using AssetHub.Endpoints;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Infrastructure.Data;
using Dam.Infrastructure.Repositories;
using Dam.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Minio;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server (.NET 9 template style)
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// Database
var connectionString = builder.Configuration.GetConnectionString("Postgres");
builder.Services.AddDbContext<AssetHubDbContext>(options =>
    options.UseNpgsql(connectionString));

// Hangfire
builder.Services.AddHangfire(config =>
{
    config.UsePostgreSqlStorage(builder.Configuration.GetConnectionString("Postgres") ?? "");
});
builder.Services.AddHangfireServer();

// MudBlazor
builder.Services.AddMudServices();

// Application Services
builder.Services.AddScoped<ICollectionAuthorizationService, CollectionAuthorizationService>();
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();
builder.Services.AddScoped<ICollectionAclRepository, CollectionAclRepository>();
//builder.Services.AddScoped<IAssetRepository, AssetRepository>();
//builder.Services.AddScoped<IMinIOAdapter, MinIOAdapter>();
//builder.Services.AddScoped<IMediaProcessingService, MediaProcessingService>();

//// MinIO client
//var minioSettings = builder.Configuration.GetSection("MinIO");
//var minioEndpoint = minioSettings["Endpoint"] ?? "localhost:9000";
//var minioAccessKey = minioSettings["AccessKey"] ?? "minioadmin";
//var minioSecretKey = minioSettings["SecretKey"] ?? "minioadmin";
//var minioUseSsl = minioSettings.GetValue("UseSsl", false);
//
//var minioClient = new Minio.MinioClient()
//    .WithEndpoint(minioEndpoint)
//    .WithCredentials(minioAccessKey, minioSecretKey)
//    .WithSSL(minioUseSsl)
//    .Build();
//
//builder.Services.AddSingleton(minioClient);

// AuthN/AuthZ
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "__Host.assethub.auth";
    options.Cookie.SameSite = SameSiteMode.Lax;
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    // --- Keycloak OIDC endpoints ---
    var keycloakConfig = builder.Configuration.GetSection("Keycloak");
    
    // Authority for server-side metadata discovery (uses Docker network DNS)
    options.Authority = keycloakConfig["Authority"] ?? "http://keycloak:8080/realms/media";
    options.ClientId = keycloakConfig["ClientId"] ?? "assethub-app";
    
    // For browser-based requests, use localhost (not Docker-internal hostname)
    // This is loaded dynamically when the browser needs to redirect to Keycloak
    options.MetadataAddress = "http://localhost:8080/realms/media/.well-known/openid-configuration";

    // .NET dev: Keycloak kör oftast http lokalt
    options.RequireHttpsMetadata = false;
    
    options.CallbackPath = "/signin-oidc";
    options.SignedOutCallbackPath = "/signout-callback-oidc";
    options.ResponseMode = OpenIdConnectResponseMode.FormPost;

    // Using public client with PKCE (no client secret)
    options.UsePkce = true;

    options.ResponseType = OpenIdConnectResponseType.Code;

    // Spara tokens om du vill kunna kalla APIs senare med access token
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;

    // Scopes (openid krävs)
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    // Viktigt för att undvika konstig claim-mappning
    options.MapInboundClaims = false;

    // Token validation / roles
    options.TokenValidationParameters = new TokenValidationParameters
    {
        NameClaimType = "preferred_username",  // Keycloak har ofta preferred_username
        RoleClaimType = ClaimTypes.Role        // vi mappar in ClaimTypes.Role nedan
    };

    options.Events = new OpenIdConnectEvents
    {
        OnTokenValidated = context =>
        {
            // Keycloak roller finns typiskt i:
            // 1) realm_access.roles
            // 2) resource_access["<clientId>"].roles
            //
            // Vi plockar båda och lägger som ClaimTypes.Role så [Authorize(Roles="x")] funkar.

            if (context.Principal?.Identity is not ClaimsIdentity identity)
                return Task.CompletedTask;

            // Helper: lägg till roll om den inte redan finns
            static void AddRoleIfMissing(ClaimsIdentity id, string role)
            {
                if (!id.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == role))
                    id.AddClaim(new Claim(ClaimTypes.Role, role));
            }

            // 1) Realm roles: claim kan komma som JSON i "realm_access"
            // Keycloak kan leverera detta via id_token eller userinfo.
            var realmAccess = identity.FindFirst("realm_access")?.Value;
            if (!string.IsNullOrWhiteSpace(realmAccess))
            {
                foreach (var role in ExtractRolesFromKeycloakJson(realmAccess))
                    AddRoleIfMissing(identity, role);
            }

            // 2) Client roles: "resource_access" innehåller roller per client
            var resourceAccess = identity.FindFirst("resource_access")?.Value;
            if (!string.IsNullOrWhiteSpace(resourceAccess))
            {
                foreach (var role in ExtractClientRolesFromKeycloakJson(resourceAccess, "assethub-app"))
                    AddRoleIfMissing(identity, role);
            }

            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/auth/login", async (HttpContext http) =>
{
    await http.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new() { RedirectUri = "/" });
});

app.MapGet("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new() { RedirectUri = "/" });
});

// API Endpoints
app.MapCollectionEndpoints();
//app.MapAssetEndpoints();
//app.MapShareEndpoints();

// Blazor endpoints
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

// Hangfire Dashboard
app.MapHangfireDashboard();

app.Run();


// ------------------
// Minimal JSON helpers (ingen extra lib behövs)
// ------------------
static IEnumerable<string> ExtractRolesFromKeycloakJson(string json)
{
    // realm_access: {"roles":["media-admin","media-user", ...]}
    // Vi kör extremt enkel parsing utan att ta in JSON-lib i exemplet.
    // Robustare: använd System.Text.Json (se kommentar nedan).
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("roles", out var rolesProp) || rolesProp.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Array.Empty<string>();

        return rolesProp.EnumerateArray()
            .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }
    catch
    {
        return Array.Empty<string>();
    }
}

static IEnumerable<string> ExtractClientRolesFromKeycloakJson(string json, string clientId)
{
    // resource_access: {"assethub-app":{"roles":["x","y"]}, "account": {...}}
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(clientId, out var clientObj) || clientObj.ValueKind != System.Text.Json.JsonValueKind.Object)
            return Array.Empty<string>();

        if (!clientObj.TryGetProperty("roles", out var rolesProp) || rolesProp.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Array.Empty<string>();

        return rolesProp.EnumerateArray()
            .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }
    catch
    {
        return Array.Empty<string>();
    }
}
