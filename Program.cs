using System.Security.Claims;
using System.Net;
using Dam.Ui;
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
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server (.NET 9 template style)
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();

// Database with Npgsql dynamic JSON support for Dictionary<string, object> in jsonb columns
var connectionString = builder.Configuration.GetConnectionString("Postgres");
var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AssetHubDbContext>(options =>
    options.UseNpgsql(dataSource));

// Hangfire
builder.Services.AddHangfire(config =>
{
    config.UsePostgreSqlStorage(builder.Configuration.GetConnectionString("Postgres") ?? "");
});
builder.Services.AddHangfireServer();

// MudBlazor
builder.Services.AddMudServices();

// Configuration
builder.Services.Configure<MinIOSettings>(builder.Configuration.GetSection(MinIOSettings.SectionName));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));

// Application Services
builder.Services.AddScoped<ICollectionAuthorizationService, CollectionAuthorizationService>();
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();
builder.Services.AddScoped<ICollectionAclRepository, CollectionAclRepository>();
builder.Services.AddScoped<IAssetRepository, AssetRepository>();
builder.Services.AddScoped<IShareRepository, ShareRepository>();
builder.Services.AddScoped<IMinIOAdapter, MinIOAdapter>();
builder.Services.AddScoped<IMediaProcessingService, MediaProcessingService>();
builder.Services.AddScoped<IUserLookupService, UserLookupService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// UI Services
builder.Services.AddScoped<Dam.Ui.Services.IUserFeedbackService, Dam.Ui.Services.UserFeedbackService>();

// HTTP Client for Blazor UI to call our own API
// In Blazor Server, we need to forward the auth cookies when making internal API calls
// Register the cookie forwarding handler for DI
builder.Services.AddTransient<Dam.Ui.Services.CookieForwardingHandler>();

builder.Services.AddHttpClient<Dam.Ui.Services.AssetHubApiClient>((sp, client) =>
{
    // Configure base address to call our own API endpoints
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor.HttpContext?.Request;
    if (request != null)
    {
        client.BaseAddress = new Uri($"{request.Scheme}://{request.Host}");
    }
    else
    {
        // Fallback for when HttpContext is not available
        client.BaseAddress = new Uri("http://localhost:7252");
    }
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Allow self-signed certs in development
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
})
.AddHttpMessageHandler<Dam.Ui.Services.CookieForwardingHandler>();

// MinIO client
var minioSettings = builder.Configuration.GetSection("MinIO");
var minioEndpoint = minioSettings["Endpoint"] ?? "localhost:9000";
var minioAccessKey = minioSettings["AccessKey"] ?? "minioadmin";
var minioSecretKey = minioSettings["SecretKey"] ?? "minioadmin";
var minioUseSsl = minioSettings.GetValue("UseSsl", false);

var minioClient = new Minio.MinioClient()
    .WithEndpoint(minioEndpoint)
    .WithCredentials(minioAccessKey, minioSecretKey)
    .WithSSL(minioUseSsl)
    .Build();

builder.Services.AddSingleton<Minio.IMinioClient>(minioClient);

// AuthN/AuthZ
// Development-only diagnostics: capture Keycloak userinfo failures (401/403/etc) with response body.
builder.Services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, AssetHub.OidcBackchannelLoggingPostConfigure>();

// Keycloak configuration
var keycloakConfig = builder.Configuration.GetSection("Keycloak");
var keycloakAuthority = keycloakConfig["Authority"] ?? "http://keycloak:8080/realms/media";
var clientSecret = keycloakConfig["ClientSecret"];
if (string.IsNullOrWhiteSpace(clientSecret))
    throw new InvalidOperationException("Keycloak:ClientSecret must be configured in appsettings. Check appsettings.json or environment variables.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "Smart"; // Use policy scheme to select based on request
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddPolicyScheme("Smart", "Smart Auth Selector", options =>
{
    // Select JWT Bearer for API requests with Authorization header, otherwise Cookie
    options.ForwardDefaultSelector = context =>
    {
        string? authorization = context.Request.Headers.Authorization;
        if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        return CookieAuthenticationDefaults.AuthenticationScheme;
    };
})
.AddJwtBearer(options =>
{
    options.Authority = keycloakAuthority;
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        // Only accept tokens issued from keycloak:8080 (Docker internal hostname)
        ValidIssuer = keycloakAuthority,
        ValidateAudience = false, // Keycloak doesn't always include audience
        ValidateLifetime = true,
        NameClaimType = "preferred_username",
        RoleClaimType = ClaimTypes.Role
    };
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = "__Host.assethub.auth";
    options.Cookie.SameSite = SameSiteMode.Lax;
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    // --- Keycloak OIDC endpoints ---
    // Using single URL for all Keycloak communication (keycloak hostname resolves to 127.0.0.1 via hosts file)
    // This works for both browser (Windows host) and container-to-container communication
    options.Authority = keycloakAuthority;
    options.MetadataAddress = keycloakAuthority + "/.well-known/openid-configuration";
    
    options.ClientId = keycloakConfig["ClientId"] ?? "assethub-app";
    
    // Client secret for confidential client flow - required for confidential clients
    options.ClientSecret = clientSecret;

    // .NET dev: Keycloak kör oftast http lokalt
    options.RequireHttpsMetadata = false;
    
    options.CallbackPath = "/signin-oidc";
    options.SignedOutCallbackPath = "/signout-callback-oidc";
    // Don't force response_mode=form_post. Query is the typical default for code flow and
    // avoids confusing scenarios where /signin-oidc is reached via GET (refresh/bookmark)
    // and would otherwise throw "message.State is null or empty".

    // If someone hits /signin-oidc without an OIDC response (no state/code), ignore it.
    // This prevents unhandled exceptions on refresh/bookmark.
    options.SkipUnrecognizedRequests = true;

    // Using confidential client (with client secret)
    options.UsePkce = false;

    options.ResponseType = OpenIdConnectResponseType.Code;

    // Spara tokens om du vill kunna kalla APIs senare med access token
    options.SaveTokens = true;
    // SECURITY: Fetch claims from userinfo endpoint for server-side verification
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
        RoleClaimType = ClaimTypes.Role,       // vi mappar in ClaimTypes.Role nedan
        ValidIssuer = keycloakAuthority  // Single issuer since we use consistent keycloak:8080 URL
    };

    // No URL rewriting needed - using single consistent URL via hosts file
    options.Events = new OpenIdConnectEvents
    {
        OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(context.Failure, $"OIDC remote failure: {context.Failure?.Message}");

            // Avoid unhandled exception page in dev; bounce home with a lightweight signal.
            context.HandleResponse();
            context.Response.Redirect("/?authError=oidc_remote_failure");
            return Task.CompletedTask;
        },
        OnTokenResponseReceived = context =>
        {
            // Diagnostics: confirm token endpoint returned what we expect.
            // Do NOT log tokens; just log presence/length.
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var accessTokenLen = context.TokenEndpointResponse?.AccessToken?.Length ?? 0;
            var idTokenLen = context.TokenEndpointResponse?.IdToken?.Length ?? 0;
            var tokenType = context.TokenEndpointResponse?.TokenType;

            logger.LogInformation($"OIDC token response received. token_type={tokenType}, access_token_len={accessTokenLen}, id_token_len={idTokenLen}");

            return Task.CompletedTask;
        },
        OnUserInformationReceived = async context =>
        {
            // SECURITY: This is called after successfully retrieving user info from the userinfo endpoint
            // This ensures the backend has verified claims directly with the identity provider
            await Task.CompletedTask;
        },
        OnAuthenticationFailed = async context =>
        {
            // Log authentication failures for debugging
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError($"Authentication failed: {context.Exception?.Message ?? "Unknown error"}. Exception: {context.Exception?.InnerException?.Message ?? "No inner exception"}");

            // Prevent a 500/developer exception page on auth errors; redirect to home.
            context.HandleResponse();
            context.Response.Redirect("/?authError=authentication_failed");
            await Task.CompletedTask;
        },
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

// Configure authorization policies based on Keycloak realm roles
builder.Services.AddAuthorization(options =>
{
    // Policy requiring the user to be authenticated (default)
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
    
    // Role-based policies - these use Keycloak realm roles mapped to ClaimTypes.Role
    options.AddPolicy("RequireViewer", policy => 
        policy.RequireAssertion(context => 
            context.User.IsInRole("viewer") || 
            context.User.IsInRole("contributor") || 
            context.User.IsInRole("manager") || 
            context.User.IsInRole("admin")));
    
    options.AddPolicy("RequireContributor", policy => 
        policy.RequireAssertion(context => 
            context.User.IsInRole("contributor") || 
            context.User.IsInRole("manager") || 
            context.User.IsInRole("admin")));
    
    options.AddPolicy("RequireManager", policy => 
        policy.RequireAssertion(context => 
            context.User.IsInRole("manager") || 
            context.User.IsInRole("admin")));
    
    options.AddPolicy("RequireAdmin", policy => 
        policy.RequireRole("admin"));
});

var app = builder.Build();

// Always log a build stamp so it's easy to verify which binary is running.
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BuildStamp");
    logger.LogInformation($"AssetHub starting. BuildStamp={Dam.Application.BuildInfo.Stamp}. Environment={app.Environment.EnvironmentName}");
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Development-only: log what actually arrives at the OIDC callback.
// Helpful for diagnosing missing state/code and content-type mismatches.
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path == "/signin-oidc")
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OidcCallback");

            logger.LogInformation($"OIDC callback received: {context.Request.Method} {context.Request.Path}{context.Request.QueryString}. ContentType={context.Request.ContentType ?? "<null>"}");

            try
            {
                context.Request.EnableBuffering();

                if (context.Request.HasFormContentType)
                {
                    var form = await context.Request.ReadFormAsync(context.RequestAborted);
                    logger.LogInformation($"OIDC callback form keys: {string.Join(", ", form.Keys.OrderBy(k => k, StringComparer.Ordinal))}");
                    logger.LogInformation($"OIDC callback has state={form.TryGetValue("state", out var state) && !string.IsNullOrWhiteSpace(state.ToString())}, code={form.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code.ToString())}");
                }
                else
                {
                    logger.LogInformation($"OIDC callback query keys: {string.Join(", ", context.Request.Query.Keys.OrderBy(k => k, StringComparer.Ordinal))}");
                    logger.LogInformation($"OIDC callback has state={context.Request.Query.ContainsKey("state") && !string.IsNullOrWhiteSpace(context.Request.Query["state"].ToString())}, code={context.Request.Query.ContainsKey("code") && !string.IsNullOrWhiteSpace(context.Request.Query["code"].ToString())}");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"Failed to inspect OIDC callback request");
            }
            finally
            {
                if (context.Request.Body.CanSeek)
                    context.Request.Body.Position = 0;
            }
        }

        // Prevent auth callback exceptions from bubbling to the Developer Exception Page.
        // We still log via the OIDC events/backchannel logger.
        if (context.Request.Path == "/signin-oidc")
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OidcCallback");
                logger.LogError(ex, $"Unhandled exception during /signin-oidc pipeline: {ex.Message}");

                if (!context.Response.HasStarted)
                {
                    context.Response.Redirect("/?authError=signin_oidc_exception");
                    return;
                }
                throw;
            }
        }
        else
        {
            await next();
        }
    });
}
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Simple build stamp endpoint to confirm which binary is running.
app.MapGet("/__build", () => Results.Json(new { stamp = Dam.Application.BuildInfo.Stamp, environment = app.Environment.EnvironmentName }));

// Development-only: verify Keycloak userinfo via direct API calls (token + userinfo).
// Local-only guard prevents exposing this outside localhost.
app.MapGet("/debug/ping", (HttpContext http) =>
    {
        if (!Dam.Application.DebugGuard.IsLocalDebugRequest(http))
            return Results.NotFound();

        var env = app.Environment.EnvironmentName;
        var asm = typeof(Program).Assembly;
        var version = asm.GetName().Version?.ToString() ?? "<unknown>";
        return Results.Json(new { ok = true, environment = env, version, remoteIp = http.Connection.RemoteIpAddress?.ToString(), host = http.Request.Host.Value });
    })
    .AllowAnonymous();

app.MapPost("/debug/keycloak/userinfo-probe", async (HttpContext http, IConfiguration config) =>
    {
        if (!Dam.Application.DebugGuard.IsLocalDebugRequest(http))
            return Results.NotFound();

        var authority = config["Keycloak:Authority"] ?? "http://keycloak:8080/realms/media";
        var clientId = config["Keycloak:ClientId"] ?? "assethub-app";
        var clientSecret = config["Keycloak:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientSecret))
            return Results.BadRequest(new { error = "Missing Keycloak:ClientSecret" });

        var payload = await http.Request.ReadFromJsonAsync<UserInfoProbeRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.Username) || string.IsNullOrWhiteSpace(payload.Password))
            return Results.BadRequest(new { error = "Provide JSON: { username, password, scope? }" });

        using var httpClient = new System.Net.Http.HttpClient();
        var tokenUrl = authority.TrimEnd('/') + "/protocol/openid-connect/token";
        var userInfoUrl = authority.TrimEnd('/') + "/protocol/openid-connect/userinfo";

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["username"] = payload.Username,
            ["password"] = payload.Password,
            ["scope"] = string.IsNullOrWhiteSpace(payload.Scope) ? "openid profile email" : payload.Scope
        };

        using var tokenResp = await httpClient.PostAsync(tokenUrl, new System.Net.Http.FormUrlEncodedContent(form));
        var tokenBody = await tokenResp.Content.ReadAsStringAsync();

        if (!tokenResp.IsSuccessStatusCode)
        {
            return Results.Json(new
            {
                token = new { status = (int)tokenResp.StatusCode, body = tokenBody },
                userinfo = (object?)null
            }, statusCode: (int)tokenResp.StatusCode);
        }

        string? accessToken = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(tokenBody);
            if (doc.RootElement.TryGetProperty("access_token", out var at))
                accessToken = at.GetString();
        }
        catch
        {
            // ignore parse errors; handled below
        }

        if (string.IsNullOrWhiteSpace(accessToken))
            return Results.Json(new { error = "Token response missing access_token", tokenBody });

        using var userReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, userInfoUrl);
        userReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var userResp = await httpClient.SendAsync(userReq);
        var userBody = await userResp.Content.ReadAsStringAsync();

        return Results.Json(new
        {
            token = new
            {
                status = (int)tokenResp.StatusCode,
                access_token_preview = accessToken.Length <= 16 ? accessToken : accessToken[..16] + "…",
            },
            userinfo = new
            {
                endpoint = userInfoUrl,
                status = (int)userResp.StatusCode,
                www_authenticate = userResp.Headers.WwwAuthenticate.Count > 0 ? string.Join(", ", userResp.Headers.WwwAuthenticate) : null,
                body = userBody
            }
        });
    })
    .AllowAnonymous();

// If the OIDC handler skips an unrecognized callback request, this provides a clearer response.
app.MapMethods("/signin-oidc", new[] { "GET", "POST" }, () =>
        Results.BadRequest("OIDC callback hit without state/code. Start login via /auth/login."))
    .AllowAnonymous();

app.MapGet("/auth/login", async (HttpContext http, string? returnUrl) =>
{
    var redirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
    await http.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new() { RedirectUri = redirectUri });
});

app.MapGet("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new() { RedirectUri = "/" });
});

// API Endpoints
app.MapCollectionEndpoints();
app.MapAssetEndpoints();
app.MapShareEndpoints();
app.MapAdminEndpoints();

// Blazor endpoints
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

// Hangfire Dashboard
app.MapHangfireDashboard();

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

app.Run();


internal sealed record UserInfoProbeRequest(string Username, string Password, string? Scope);
