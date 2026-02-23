using System.Security.Claims;
using AssetHub.Api.Endpoints;
using AssetHub.Api.Middleware;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Ui;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;
using Hangfire.Dashboard;
using Serilog;

namespace AssetHub.Api.Extensions;

/// <summary>
/// Extension methods for configuring the WebApplication middleware pipeline,
/// startup tasks, and endpoint mapping.
/// </summary>
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

        await MigrateDatabaseAsync(scope.ServiceProvider, logger);
        await EnsureMinioBucketAsync(scope.ServiceProvider, app.Configuration, logger);

        LogBuildStamp(app);
    }

    // ── Middleware pipeline ─────────────────────────────────────────────────

    /// <summary>
    /// Configures the complete middleware pipeline (HTTPS, exception handling,
    /// auth, logging, static files, etc.).
    /// </summary>
    public static void UseAssetHubMiddleware(this WebApplication app)
    {
        // Process X-Forwarded-* headers from reverse proxy (must be first).
        // KnownNetworks/KnownProxies are cleared so the proxy IP on the Docker
        // bridge network is trusted. This is safe because the app is only
        // reachable via the reverse proxy; direct external access is blocked.
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        forwardedOptions.KnownNetworks.Clear();
        forwardedOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedOptions);

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
            app.UseHsts();
        }

        // Security headers
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
                // CSP for Blazor Server: allow self + inline scripts/styles (Blazor, MudBlazor) + wss for SignalR
                headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self' 'unsafe-inline'; " +
                    "style-src 'self' 'unsafe-inline'; " +
                    "img-src 'self' data: blob:; " +
                    "font-src 'self'; " +
                    "connect-src 'self' wss:; " +
                    "frame-ancestors 'none'; " +
                    "base-uri 'self'; " +
                    "form-action 'self';";
            }

            await next();
        });

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
        app.UseRateLimiter();
        app.UseAuthentication();

        // Allow anonymous access to Blazor framework files and the SignalR hub.
        // Required for anonymous pages like /share/{token} to load and establish
        // an interactive Blazor circuit without triggering an OIDC auth redirect.
        // We add AllowAnonymous (rather than stripping IAuthorizeData) because the
        // FallbackPolicy requires authentication on endpoints with no auth metadata.
        // Page-level authorization is still enforced by individual Blazor components
        // via [Authorize]/[AllowAnonymous] attributes — the hub is just the transport.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (path != null &&
                (path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)))
            {
                var endpoint = context.GetEndpoint();
                if (endpoint != null)
                {
                    context.SetEndpoint(new Endpoint(
                        endpoint.RequestDelegate,
                        new EndpointMetadataCollection(
                            endpoint.Metadata.Append(new AllowAnonymousAttribute())),
                        endpoint.DisplayName));
                }
            }
            await next();
        });

        app.UseAuthorization();

        // Required for Blazor Server interactive rendering. Does NOT blanket-enforce
        // on Minimal API endpoints that use [FromBody] JSON — only on endpoints using
        // [FromForm] or Razor Component form handling. API endpoints designed for
        // external (JWT Bearer) or anonymous consumers explicitly call .DisableAntiforgery().
        app.UseAntiforgery();
    }

    // ── Endpoint mapping ────────────────────────────────────────────────────

    /// <summary>
    /// Maps all API endpoints, auth routes, Blazor, Hangfire, and health checks.
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

        // Auth routes
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

        // API endpoints
        app.MapDashboardEndpoints();
        app.MapCollectionEndpoints();
        app.MapAssetEndpoints();
        app.MapShareEndpoints();
        app.MapAdminEndpoints();
        app.MapZipDownloadEndpoints();

        // Blazor
        app.MapRazorComponents<App>()
           .AddInteractiveServerRenderMode();

        // Hangfire Dashboard (admin-only)
        app.MapHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[] { new HangfireAdminAuthorizationFilter() }
        });

        // Recurring jobs
        RecurringJob.AddOrUpdate<IUserSyncService>(
            "sync-deleted-users",
            service => service.SyncDeletedUsersAsync(false, CancellationToken.None),
            Cron.Daily);

        RecurringJob.AddOrUpdate<IZipBuildService>(
            "cleanup-expired-zip-downloads",
            service => service.CleanupExpiredAsync(CancellationToken.None),
            Cron.Hourly);

        // Health checks
        MapHealthCheckEndpoints(app);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static async Task MigrateDatabaseAsync(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            var db = services.GetRequiredService<AssetHub.Infrastructure.Data.AssetHubDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                    pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied successfully.");
            }
            else
            {
                logger.LogInformation("Database is up to date — no pending migrations.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to apply database migrations. The application will start " +
                "but may not function correctly.");
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
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ApiError
                    {
                        Code = "UNAUTHORIZED",
                        Message = "Authentication required"
                    });
                }
            }
            catch (StorageException storageEx) when (context.Request.Path.StartsWithSegments("/api"))
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandler");
                
                var correlationId = context.TraceIdentifier;
                logger.LogError(storageEx, "Storage service error on {Method} {Path} [CorrelationId: {CorrelationId}]",
                    context.Request.Method, context.Request.Path, correlationId);

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ApiError
                    {
                        Code = "SERVICE_UNAVAILABLE",
                        Message = storageEx.Message,
                        Details = new Dictionary<string, string> { ["correlationId"] = correlationId }
                    });
                }
            }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException badEx) when (context.Request.Path.StartsWithSegments("/api"))
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandler");
                
                var correlationId = context.TraceIdentifier;
                logger.LogWarning(badEx, "Bad request on {Method} {Path} [CorrelationId: {CorrelationId}]",
                    context.Request.Method, context.Request.Path, correlationId);

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ApiError
                    {
                        Code = "BAD_REQUEST",
                        Message = "The request was invalid. Please check your input and try again.",
                        Details = new Dictionary<string, string> { ["correlationId"] = correlationId }
                    });
                }
            }
            catch (InvalidOperationException configEx) when (
                context.Request.Path.StartsWithSegments("/api") && 
                configEx.Message.Contains("configuration", StringComparison.OrdinalIgnoreCase))
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandler");
                
                var correlationId = context.TraceIdentifier;
                logger.LogCritical(configEx, "Configuration error on {Method} {Path} [CorrelationId: {CorrelationId}]",
                    context.Request.Method, context.Request.Path, correlationId);

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ApiError
                    {
                        Code = "CONFIGURATION_ERROR",
                        Message = "The service is misconfigured. Please contact support.",
                        Details = new Dictionary<string, string> { ["correlationId"] = correlationId }
                    });
                }
            }
            catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandler");
                
                var correlationId = context.TraceIdentifier;
                logger.LogError(ex, "Unhandled exception on {Method} {Path} [CorrelationId: {CorrelationId}]",
                    context.Request.Method, context.Request.Path, correlationId);

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ApiError
                    {
                        Code = "SERVER_ERROR",
                        Message = "An unexpected error occurred. Please try again or contact support.",
                        Details = new Dictionary<string, string> { ["correlationId"] = correlationId }
                    });
                }
            }
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

    /// <summary>
    /// Hangfire dashboard authorization filter: requires authenticated admin user.
    /// </summary>
    private sealed class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            var user = httpContext.User;
            return user.Identity?.IsAuthenticated == true
                   && user.IsInRole("admin");
        }
    }
}
