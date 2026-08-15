using Microsoft.AspNetCore.Mvc;
using AssetHub.Application.Configuration;
using AssetHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using AssetHub.Api.Endpoints;
using AssetHub.Api.Middleware;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Ui;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;
using Serilog;

namespace AssetHub.Api.Extensions;

/// <summary>
/// Extension methods for configuring the WebApplication middleware pipeline,
/// startup tasks, and endpoint mapping.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "Middleware + endpoint composition root — by definition references every endpoint module + middleware.")]
public static class WebApplicationExtensions
{
    // ── Startup tasks ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs database migration, ensures the MinIO bucket exists, and logs the build stamp.
    /// </summary>
    public static async Task RunStartupTasksAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");

        var autoMigrate = app.Configuration.GetValue("Database:AutoMigrate", true);
        await MigrateDatabaseAsync(scope.ServiceProvider, logger, autoMigrate);
        await EnsureMinioBucketAsync(scope.ServiceProvider, app.Configuration, logger);
        await SeedIdentityAsync(scope.ServiceProvider, logger);

        LogBuildStamp(app);
    }

    /// <summary>
    /// Creates the application roles and, on an empty user store, the bootstrap
    /// admin. Runs only under the Identity provider — a no-op under Keycloak,
    /// which owns its own users.
    /// </summary>
    private static async Task SeedIdentityAsync(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
    {
        var auth = services.GetRequiredService<IOptions<AuthSettings>>().Value;
        if (!auth.UsesIdentity) return;

        try
        {
            var seeder = services.GetRequiredService<IdentitySeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            // Refuse to start rather than run with no way to sign in.
            logger.LogCritical(ex, "Identity seeding failed — the application cannot start without an administrator");
            throw;
        }
    }

    // ── Middleware pipeline ─────────────────────────────────────────────────

    /// <summary>
    /// Configures the complete middleware pipeline (HTTPS, exception handling,
    /// auth, logging, static files, etc.).
    /// </summary>
    public static void UseAssetHubMiddleware(this WebApplication app)
    {
        UseForwardedHeaders(app);
        app.UseResponseCompression();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
            app.UseHsts();
        }

        UseSecurityHeaders(app);

        if (app.Environment.IsDevelopment())
        {
            app.UseMiddleware<OidcCallbackDiagnosticsMiddleware>();
        }

        app.UseGlobalExceptionHandler();
        app.UseStaticFiles();

        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "-");
                diagnosticContext.Set("UserAgent",
                    httpContext.Request.Headers.UserAgent.FirstOrDefault() ?? "-");
                if (httpContext.User.Identity?.IsAuthenticated == true)
                {
                    diagnosticContext.Set("UserId",
                        httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "-");
                }
            };
        });

        app.UseRequestLocalization();
        UseBlazorRateLimiting(app);
        app.UseRateLimiter();
        app.UseAuthentication();
        UseBlazorAnonymousAccess(app);
        app.UseAuthorization();

        // Required for Blazor Server interactive rendering. Does NOT blanket-enforce
        // on Minimal API endpoints that use [FromBody] JSON — only on endpoints using
        // [FromForm] or Razor Component form handling. Endpoints designed for JWT Bearer
        // or anonymous consumers explicitly call .DisableAntiforgery().
        app.UseAntiforgery();
    }

    /// <summary>
    /// Configures forwarded headers with RFC 1918 trusted networks for reverse proxy support.
    /// </summary>
    private static void UseForwardedHeaders(WebApplication app)
    {
        // Process X-Forwarded-* headers from reverse proxy (must be first).
        // Restricted to RFC 1918 private ranges so only the Docker reverse proxy
        // (which lives on the internal bridge network) can influence the client IP.
        // Accepting X-Forwarded-For from arbitrary sources would allow any client to
        // spoof their IP address and bypass IP-based rate limiting (CWE-807).
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        // Trust only RFC 1918 private ranges — Docker bridge (172.16.0.0/12),
        // loopback (127.0.0.0/8), class-A private (10.0.0.0/8), and
        // class-C private (192.168.0.0/16).
        // These are intentional well-known network addresses for proxy trust, not secrets.
#pragma warning disable S1313 // Hardcoded IPs are RFC 1918 private network ranges for reverse proxy trust
        forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
        forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
        forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
        forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("127.0.0.0"), 8));
#pragma warning restore S1313
        app.UseForwardedHeaders(forwardedOptions);

        // Detect a misconfigured proxy on first forwarded request (A-9). If
        // we ever see a request whose immediate connection comes from a
        // public IP — i.e., not in any of the trusted RFC 1918 ranges above
        // — the trust list doesn't match this deployment's topology and
        // every audit row downstream will collapse onto the proxy IP. Log
        // once per process so SREs see the misconfiguration without log
        // flooding.
        var proxyMismatchLogged = 0;
        app.Use(async (context, next) =>
        {
            if (Interlocked.CompareExchange(ref proxyMismatchLogged, 0, 0) == 0)
            {
                var remote = context.Connection.RemoteIpAddress;
                if (remote is not null
                    && !AssetHub.Application.Helpers.PrivateNetworkGuard.IsPrivateOrInternal(remote)
                    && Interlocked.Exchange(ref proxyMismatchLogged, 1) == 0)
                {
                    var logger = app.Services.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ForwardedHeaders");
                    logger.LogWarning(
                        "Connection.RemoteIpAddress {Address} is outside the configured trusted proxy " +
                        "networks (RFC 1918 + loopback). Either the request bypassed the reverse proxy, " +
                        "or the proxy network is not in KnownNetworks — audit IPs and IP-based rate limits " +
                        "will reflect the proxy address, not the real client.",
                        remote);
                }
            }
            await next();
        });
    }

    /// <summary>
    /// Adds security response headers (X-Content-Type-Options, CSP, etc.).
    /// </summary>
    private static void UseSecurityHeaders(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["X-XSS-Protection"] = "1; mode=block";

            if (!app.Environment.IsDevelopment())
            {
                // CSP for Blazor Server: allow self + inline scripts/styles (Blazor, MudBlazor) + wss for SignalR.
                // object-src 'none' explicitly forbids <object>/<embed>/<applet> — defends
                // against legacy plugin-based exploits + drops a vector for SVG-script
                // execution via <object data="..."> (A-11 in the security review).
                headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self' 'unsafe-inline'; " +
                    "style-src 'self' 'unsafe-inline'; " +
                    "img-src 'self' data: blob:; " +
                    "font-src 'self'; " +
                    "connect-src 'self' wss:; " +
                    "object-src 'none'; " +
                    "frame-ancestors 'none'; " +
                    "base-uri 'self'; " +
                    "form-action 'self';";
            }

            await next();
        });
    }

    /// <summary>
    /// Applies BlazorSignalR rate limiting policy to /_blazor connections
    /// to prevent WebSocket exhaustion attacks from anonymous clients.
    /// </summary>
    private static void UseBlazorRateLimiting(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (path is not null && path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase))
            {
                AddEndpointMetadata(context, new EnableRateLimitingAttribute(Constants.RateLimitPolicies.BlazorSignalR));
            }
            await next();
        });
    }

    /// <summary>
    /// Allows anonymous access to Blazor framework files and the SignalR hub.
    /// Required for anonymous pages like /share/{token} to load and establish
    /// an interactive Blazor circuit without triggering an OIDC auth redirect.
    /// </summary>
    private static void UseBlazorAnonymousAccess(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (path is not null &&
                (path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)))
            {
                AddEndpointMetadata(context, new AllowAnonymousAttribute());
            }
            await next();
        });
    }

    private static void AddEndpointMetadata(HttpContext context, object metadata)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is not null)
        {
            context.SetEndpoint(new Endpoint(
                endpoint.RequestDelegate,
                new EndpointMetadataCollection(endpoint.Metadata.Append(metadata)),
                endpoint.DisplayName));
        }
    }

    // ── Endpoint mapping ────────────────────────────────────────────────────

    /// <summary>
    /// Maps all API endpoints, auth routes, Blazor, and health checks.
    /// </summary>
    public static void MapAssetHubEndpoints(this WebApplication app)
    {
        // Build stamp (authenticated, excludes environment name from response)
        app.MapGet("/__build", () =>
            Results.Json(new
            {
                stamp = AssetHub.Application.BuildInfo.Stamp
            })).RequireAuthorization();

        // OIDC callback fallback
        app.MapMethods("/signin-oidc", new[] { "GET", "POST" }, () =>
                Results.BadRequest("OIDC callback hit without state/code. Start login via /auth/login."))
            .AllowAnonymous();

        // Auth routes. The paths are identical under both providers so the UI
        // (MainLayout, Login.razor) never learns which one is configured — only
        // what each path does behind the scenes differs.
        var authSettings = app.Services.GetRequiredService<IOptions<AuthSettings>>().Value;
        if (authSettings.UsesIdentity)
            MapIdentityAuthRoutes(app);
        else
            MapKeycloakAuthRoutes(app);

        // API endpoints
        app.MapDashboardEndpoints();
        app.MapCollectionEndpoints();
        app.MapAssetEndpoints();
        app.MapShareEndpoints();
        app.MapAdminEndpoints();
        app.MapZipDownloadEndpoints();
        app.MapMigrationEndpoints();
        app.MapAssetSearchEndpoints();
        app.MapAdminTrashEndpoints();
        app.MapAssetVersionEndpoints();

        // Blazor
        app.MapRazorComponents<App>()
           .AddInteractiveServerRenderMode();

        // Health checks
        MapHealthCheckEndpoints(app);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static async Task MigrateDatabaseAsync(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger, bool autoMigrate)
    {
        // Intentionally NOT wrapped in try/catch: a migration failure leaves the
        // schema in an unknown state. Starting the app anyway means it serves
        // requests against a stale schema (silent data corruption / 500 storms).
        // Fail fast — let the orchestrator restart and surface the failure.
        var db = services.GetRequiredService<AssetHub.Infrastructure.Data.AssetHubDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            if (autoMigrate)
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                    pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied successfully.");
            }
            else
            {
                logger.LogWarning(
                    "Database has {Count} pending migration(s) but auto-migration is disabled (Database:AutoMigrate=false). " +
                    "Pending: {Migrations}. Run migrations manually before deploying.",
                    pending.Count, string.Join(", ", pending));
            }
        }
        else
        {
            logger.LogInformation("Database is up to date — no pending migrations.");
        }
    }

    private static async Task EnsureMinioBucketAsync(
        IServiceProvider services, IConfiguration configuration, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            var minio = services.GetRequiredService<IMinioClient>();
            var bucketName = configuration["MinIO:BucketName"] ?? "assethub";
            var bucketExists = await minio.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucketName));
            if (!bucketExists)
            {
                logger.LogInformation("Creating MinIO bucket '{Bucket}'...", bucketName);
                await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
                logger.LogInformation("MinIO bucket '{Bucket}' created.", bucketName);
            }
            else
            {
                logger.LogInformation("MinIO bucket '{Bucket}' already exists.", bucketName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to verify/create MinIO bucket. The application will start " +
                "but uploads may fail.");
        }
    }

    private static void LogBuildStamp(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BuildStamp");
        logger.LogInformation("AssetHub starting. BuildStamp={Stamp}. Environment={Env}",
            AssetHub.Application.BuildInfo.Stamp, app.Environment.EnvironmentName);
    }



    private static void UseGlobalExceptionHandler(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (UnauthorizedAccessException)
            {
                await WriteErrorResponseAsync(context, StatusCodes.Status401Unauthorized,
                    "UNAUTHORIZED", "Authentication required");
            }
            catch (StorageException storageEx) when (context.Request.Path.StartsWithSegments("/api"))
            {
                LogApiException(context, storageEx, LogLevel.Error, "Storage service error");
                await WriteErrorResponseAsync(context, StatusCodes.Status503ServiceUnavailable,
                    "SERVICE_UNAVAILABLE", storageEx.Message);
            }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException badEx) when (context.Request.Path.StartsWithSegments("/api"))
            {
                LogApiException(context, badEx, LogLevel.Warning, "Bad request");
                await WriteErrorResponseAsync(context, StatusCodes.Status400BadRequest,
                    "BAD_REQUEST", "The request was invalid. Please check your input and try again.");
            }
            catch (InvalidOperationException configEx) when (
                context.Request.Path.StartsWithSegments("/api") && 
                configEx.Message.Contains("configuration", StringComparison.OrdinalIgnoreCase))
            {
                LogApiException(context, configEx, LogLevel.Critical, "Configuration error");
                await WriteErrorResponseAsync(context, StatusCodes.Status500InternalServerError,
                    "CONFIGURATION_ERROR", "The service is misconfigured. Please contact support.");
            }
            catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
            {
                LogApiException(context, ex, LogLevel.Error, "Unhandled exception");
                await WriteErrorResponseAsync(context, StatusCodes.Status500InternalServerError,
                    "SERVER_ERROR", "An unexpected error occurred. Please try again or contact support.");
            }
        });
    }

    private static void LogApiException(HttpContext context, Exception ex, LogLevel level, string description)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ApiExceptionHandler");
        
        logger.Log(level, ex, "{Description} on {Method} {Path} [CorrelationId: {CorrelationId}]",
            description, context.Request.Method, context.Request.Path, context.TraceIdentifier);
    }

    private static async Task WriteErrorResponseAsync(
        HttpContext context, int statusCode, string code, string message)
    {
        if (context.Response.HasStarted) return;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiError
        {
            Code = code,
            Message = message,
            Details = new Dictionary<string, string> { ["correlationId"] = context.TraceIdentifier }
        });
    }

    private static void MapHealthCheckEndpoints(WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponse
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponse
        }).AllowAnonymous();
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds + "ms",
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds + "ms",
                description = e.Value.Description
                // Note: exception details intentionally omitted to avoid information disclosure
            })
        };
        return context.Response.WriteAsJsonAsync(result);
    }

    /// <summary>Keycloak OIDC auth routes — unchanged from before the provider split.</summary>
    private static void MapKeycloakAuthRoutes(WebApplication app)
    {
        app.MapGet("/auth/login", async (HttpContext http, string? returnUrl) =>
        {
            // Prevent open redirect: only allow known internal routes
            var redirectUri = AssetHub.Application.Helpers.UrlSafetyHelper.SafeReturnUrl(returnUrl);
            await http.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
                new() { RedirectUri = redirectUri });
        });

        app.MapGet("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
                new() { RedirectUri = "/" });
        });

        app.MapGet("/auth/change-password", async (HttpContext http) =>
        {
            var properties = new AuthenticationProperties { RedirectUri = "/" };
            properties.Items["kc_action"] = "UPDATE_PASSWORD";
            await http.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
        }).RequireAuthorization();
    }

    /// <summary>
    /// Local Identity auth routes. Sign-in is a form POST rather than an OIDC
    /// challenge, so /auth/login simply forwards to the Blazor login page; the
    /// POST handler below is what actually establishes the cookie.
    /// </summary>
    private static void MapIdentityAuthRoutes(WebApplication app)
    {
        app.MapGet("/auth/login", (string? returnUrl) =>
        {
            var redirectUri = AssetHub.Application.Helpers.UrlSafetyHelper.SafeReturnUrl(returnUrl);
            return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(redirectUri)}");
        }).AllowAnonymous();

        // Form POST from the login page. Kept out of Blazor Server because
        // establishing an auth cookie needs a real HTTP response, which an
        // interactive circuit cannot produce.
        app.MapPost("/auth/login", async (
            HttpContext http,
            [FromForm] string userName,
            [FromForm] string password,
            [FromForm] string? returnUrl,
            [FromServices] SignInManager<AppUser> signInManager,
            [FromServices] IAuditService audit,
            CancellationToken ct) =>
        {
            var redirectUri = AssetHub.Application.Helpers.UrlSafetyHelper.SafeReturnUrl(returnUrl);
            var result = await signInManager.PasswordSignInAsync(
                userName, password, isPersistent: true, lockoutOnFailure: true);

            if (!result.Succeeded)
            {
                // Deliberately identical response for wrong-user and wrong-password
                // so the form cannot be used to enumerate accounts. Lockout is the
                // one distinguishable state, because the user needs to know to wait.
                var reason = result.IsLockedOut ? "locked" : "invalid";
                await audit.LogAsync("auth.login_failed", Constants.ScopeTypes.User, targetId: null, actorUserId: userName,
                    new() { ["reason"] = reason }, ct);
                return Results.Redirect($"/login?error={reason}&returnUrl={Uri.EscapeDataString(redirectUri)}");
            }

            return Results.Redirect(redirectUri);
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext http, [FromServices] SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/");
        }).DisableAntiforgery();

        // GET logout kept so MainLayout's existing navigation keeps working.
        app.MapGet("/auth/logout", async (HttpContext http, [FromServices] SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/");
        });

        app.MapGet("/auth/change-password", () => Results.Redirect("/reset-password/self"))
            .RequireAuthorization();

        // Self-service reset request. Always reports success: telling the caller
        // whether an account exists would make this an account-enumeration oracle.
        app.MapPost("/auth/forgot-password", async (
            [FromForm] string userNameOrEmail,
            [FromServices] UserManager<AppUser> users,
            [FromServices] IPasswordResetLinkSender<AppUser> sender,
            CancellationToken ct) =>
        {
            var user = await users.FindByNameAsync(userNameOrEmail)
                       ?? await users.FindByEmailAsync(userNameOrEmail);
            if (user is not null)
                await sender.SendAsync(user, isNewAccount: false, ct);

            return Results.Redirect("/login?reset=sent");
        }).AllowAnonymous().DisableAntiforgery();

        // Completes a reset from the emailed link.
        app.MapPost("/auth/reset-password", async (
            [FromForm] string userId,
            [FromForm] string token,
            [FromForm] string newPassword,
            [FromServices] PasswordResetLinkSender sender,
            [FromServices] IAuditService audit,
            CancellationToken ct) =>
        {
            var (ok, error) = await sender.ResetAsync(userId, token, newPassword, ct);
            if (!ok)
            {
                var reason = error is null ? "invalid" : "policy";
                return Results.Redirect(
                    $"/reset-password?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}&error={reason}");
            }

            await audit.LogAsync("user.password_reset_completed", Constants.ScopeTypes.User,
                targetId: null, actorUserId: userId, null, ct);
            return Results.Redirect("/login?reset=done");
        }).AllowAnonymous().DisableAntiforgery();
    }

}
