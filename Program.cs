using System.Security.Claims;
using System.Net;
using Dam.Ui;
using Dam.Application;
using AssetHub.Endpoints;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Infrastructure.Data;
using Dam.Infrastructure.Repositories;
using Dam.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Minio;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Kestrel limits — allow the IFormFile upload fallback path to handle files
// up to MaxUploadSizeMb. The presigned upload path bypasses this entirely.
var maxUploadMb = builder.Configuration.GetValue("App:MaxUploadSizeMb", 500);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = (long)maxUploadMb * 1024 * 1024;
});

// Allow personal overrides via appsettings.Local.json (gitignored)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Localization (Swedish & English)
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "en", "sv" };
    options.SetDefaultCulture("en")
        .AddSupportedCultures(supportedCultures)
        .AddSupportedUICultures(supportedCultures);
    // Use a cookie so the LanguageSwitcher component can persist the user's choice
    options.RequestCultureProviders.Insert(0,
        new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider());
});

// Blazor Server (.NET 9 template style)
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();

// Data Protection — persist keys to PostgreSQL so encrypted tokens survive
// restarts, redeployments, and work across replicas without shared volumes.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AssetHubDbContext>()
    .SetApplicationName("AssetHub");

// Database with Npgsql dynamic JSON support for Dictionary<string, object> in jsonb columns
var connectionString = builder.Configuration.GetConnectionString("Postgres");
var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<AssetHubDbContext>(options =>
    options.UseNpgsql(dataSource));

// Hangfire — prefer dedicated connection string, fall back to the main Postgres one
var hangfireConnectionString = builder.Configuration["Hangfire:ConnectionString"];
if (string.IsNullOrWhiteSpace(hangfireConnectionString))
    hangfireConnectionString = builder.Configuration.GetConnectionString("Postgres") ?? "";
builder.Services.AddHangfire(config =>
{
    config.UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(hangfireConnectionString));
});
builder.Services.AddHangfireServer();

// MudBlazor
builder.Services.AddMudServices();

// In-memory cache for authorization lookups, user names, asset-collection mappings
builder.Services.AddMemoryCache();

// Configuration
builder.Services.Configure<MinIOSettings>(builder.Configuration.GetSection(MinIOSettings.SectionName));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.Configure<KeycloakSettings>(builder.Configuration.GetSection(KeycloakSettings.SectionName));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(AppSettings.SectionName));
builder.Services.Configure<ImageProcessingSettings>(builder.Configuration.GetSection(ImageProcessingSettings.SectionName));

// Application Services
builder.Services.AddScoped<ICollectionAuthorizationService, CollectionAuthorizationService>();
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();
builder.Services.AddScoped<ICollectionAclRepository, CollectionAclRepository>();
builder.Services.AddScoped<IAssetRepository, AssetRepository>();
builder.Services.AddScoped<IAssetCollectionRepository, AssetCollectionRepository>();
builder.Services.AddScoped<IShareRepository, ShareRepository>();
builder.Services.AddScoped<IMinIOAdapter>(sp =>
{
    var internalClient = sp.GetRequiredService<Minio.IMinioClient>();
    var publicClient = sp.GetRequiredKeyedService<Minio.IMinioClient>("public");
    var adapterLogger = sp.GetRequiredService<ILogger<MinIOAdapter>>();
    return new MinIOAdapter(internalClient, publicClient, adapterLogger);
});
builder.Services.AddScoped<IMediaProcessingService, MediaProcessingService>();
builder.Services.AddScoped<IUserLookupService, UserLookupService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();
builder.Services.AddScoped<IAuditService, AuditService>();

// Keycloak Admin API client for user management
var keycloakTimeoutSeconds = builder.Configuration.GetValue("Keycloak:TimeoutSeconds", 30);
builder.Services.AddHttpClient<IKeycloakUserService, KeycloakUserService>(client =>
{
    // HttpClient is configured with no base address; KeycloakUserService builds full URLs from config
    client.Timeout = TimeSpan.FromSeconds(keycloakTimeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    if (builder.Environment.IsDevelopment())
    {
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }
    return handler;
});

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
        // Fallback for when HttpContext is not available — use configured BaseUrl
        var baseUrl = builder.Configuration["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("App:BaseUrl is required when HttpContext is not available. Check appsettings for the current environment.");
        client.BaseAddress = new Uri(baseUrl);
    }
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    if (builder.Environment.IsDevelopment())
    {
        // Allow self-signed certs in development
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }
    return handler;
})
.AddHttpMessageHandler<Dam.Ui.Services.CookieForwardingHandler>();

// MinIO client (internal — for server-side data operations)
var minioConfig = builder.Configuration.GetSection("MinIO");
var minioEndpoint = minioConfig["Endpoint"];
if (string.IsNullOrWhiteSpace(minioEndpoint))
    throw new InvalidOperationException("MinIO:Endpoint is required. Check appsettings for the current environment.");
var minioAccessKey = minioConfig["AccessKey"];
if (string.IsNullOrWhiteSpace(minioAccessKey))
    throw new InvalidOperationException("MinIO:AccessKey is required. Check appsettings for the current environment.");
var minioSecretKey = minioConfig["SecretKey"];
if (string.IsNullOrWhiteSpace(minioSecretKey))
    throw new InvalidOperationException("MinIO:SecretKey is required. Check appsettings for the current environment.");
var minioUseSsl = minioConfig.GetValue("UseSSL", true);

var minioClient = new Minio.MinioClient()
    .WithEndpoint(minioEndpoint)
    .WithCredentials(minioAccessKey, minioSecretKey)
    .WithSSL(minioUseSsl)
    .Build();

builder.Services.AddSingleton<Minio.IMinioClient>(minioClient);

// MinIO public client — for presigned URLs that browsers access directly.
// Falls back to the internal endpoint if PublicUrl is not configured.
var publicEndpoint = minioConfig["PublicUrl"];
var publicUseSsl = minioConfig.GetValue("PublicUseSSL", minioUseSsl);
Minio.IMinioClient publicMinioClient;
if (!string.IsNullOrWhiteSpace(publicEndpoint) && publicEndpoint != minioEndpoint)
{
    publicMinioClient = new Minio.MinioClient()
        .WithEndpoint(publicEndpoint)
        .WithCredentials(minioAccessKey, minioSecretKey)
        .WithSSL(publicUseSsl)
        .Build();
}
else
{
    publicMinioClient = minioClient; // same client when endpoint matches
}
builder.Services.AddKeyedSingleton<Minio.IMinioClient>("public", publicMinioClient);

// AuthN/AuthZ
// Development-only diagnostics: capture Keycloak userinfo failures (401/403/etc) with response body.
builder.Services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, AssetHub.OidcBackchannelLoggingPostConfigure>();

// Keycloak configuration
var keycloakConfig = builder.Configuration.GetSection("Keycloak");
var keycloakAuthority = keycloakConfig["Authority"];
if (string.IsNullOrWhiteSpace(keycloakAuthority))
    throw new InvalidOperationException("Keycloak:Authority is required. Check appsettings for the current environment.");
var clientSecret = keycloakConfig["ClientSecret"];
if (string.IsNullOrWhiteSpace(clientSecret))
    throw new InvalidOperationException("Keycloak:ClientSecret is required. Check appsettings for the current environment.");
var requireHttpsMetadata = keycloakConfig.GetValue("RequireHttpsMetadata", true);

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
    options.RequireHttpsMetadata = requireHttpsMetadata;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        // Only accept tokens issued from keycloak:8080 (Docker internal hostname)
        ValidIssuer = keycloakAuthority,
        ValidateAudience = true,
        ValidAudiences = new[] { "assethub-app", "account" }, // Keycloak audience mapper must be configured
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
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    // --- Keycloak OIDC endpoints ---
    // Using single URL for all Keycloak communication (keycloak hostname resolves to 127.0.0.1 via hosts file)
    // This works for both browser (Windows host) and container-to-container communication
    options.Authority = keycloakAuthority;
    options.MetadataAddress = keycloakAuthority + "/.well-known/openid-configuration";
    
    var clientId = keycloakConfig["ClientId"];
    if (string.IsNullOrWhiteSpace(clientId))
        throw new InvalidOperationException("Keycloak:ClientId is required. Check appsettings for the current environment.");
    options.ClientId = clientId;
    
    // Client secret for confidential client flow - required for confidential clients
    options.ClientSecret = clientSecret;

    // RequireHttpsMetadata: true in production, false in development (set in appsettings)
    options.RequireHttpsMetadata = requireHttpsMetadata;
    
    options.CallbackPath = "/signin-oidc";
    options.SignedOutCallbackPath = "/signout-callback-oidc";
    // Don't force response_mode=form_post. Query is the typical default for code flow and
    // avoids confusing scenarios where /signin-oidc is reached via GET (refresh/bookmark)
    // and would otherwise throw "message.State is null or empty".

    // If someone hits /signin-oidc without an OIDC response (no state/code), ignore it.
    // This prevents unhandled exceptions on refresh/bookmark.
    options.SkipUnrecognizedRequests = true;

    // PKCE enabled for defense-in-depth even with confidential client
    options.UsePkce = true;

    options.ResponseType = OpenIdConnectResponseType.Code;

    // Save tokens to be able to call APIs later with the access token
    options.SaveTokens = true;
    // SECURITY: Fetch claims from userinfo endpoint for server-side verification
    options.GetClaimsFromUserInfoEndpoint = true;

    // Scopes (openid is required)
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    // Important to avoid unexpected claim mapping
    options.MapInboundClaims = false;

    // Token validation / roles
    options.TokenValidationParameters = new TokenValidationParameters
    {
        NameClaimType = "preferred_username",  // Keycloak uses preferred_username
        RoleClaimType = ClaimTypes.Role,       // we map ClaimTypes.Role below
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
            // Keycloak roles are typically found in:
            // 1) realm_access.roles
            // 2) resource_access["<clientId>"].roles
            //
            // We extract both and add them as ClaimTypes.Role so [Authorize(Roles="x")] works.

            if (context.Principal?.Identity is not ClaimsIdentity identity)
                return Task.CompletedTask;

            // Helper: add role if it doesn't already exist
            static void AddRoleIfMissing(ClaimsIdentity id, string role)
            {
                if (!id.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == role))
                    id.AddClaim(new Claim(ClaimTypes.Role, role));
            }

            // 1) Realm roles: claim may arrive as JSON in "realm_access"
            // Keycloak can deliver this via id_token or userinfo.
            var realmAccess = identity.FindFirst("realm_access")?.Value;
            if (!string.IsNullOrWhiteSpace(realmAccess))
            {
                foreach (var role in ExtractRolesFromKeycloakJson(realmAccess))
                    AddRoleIfMissing(identity, role);
            }

            // 2) Client roles: "resource_access" contains roles per client
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

// Global exception handler for API endpoints — logs the real exception
// and returns a sanitized ProblemDetails response (no stack traces / internal messages).
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (UnauthorizedAccessException)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        }
    }
    catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiExceptionHandler");
        logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                title = "An unexpected error occurred",
                status = 500,
                detail = "Please try again or contact support."
            });
        }
    }
});

app.UseStaticFiles();

app.UseRequestLocalization();

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

        var authority = config["Keycloak:Authority"] ?? "";
        var clientId = config["Keycloak:ClientId"] ?? "";
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
// Minimal JSON helpers (no extra library needed)
// ------------------
static IEnumerable<string> ExtractRolesFromKeycloakJson(string json)
{
    // realm_access: {"roles":["media-admin","media-user", ...]}
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
    catch (System.Text.Json.JsonException)
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
    catch (System.Text.Json.JsonException)
    {
        return Array.Empty<string>();
    }
}

app.Run();


internal sealed record UserInfoProbeRequest(string Username, string Password, string? Scope);
