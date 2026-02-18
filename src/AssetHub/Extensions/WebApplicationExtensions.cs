using System.Security.Claims;
using AssetHub.Endpoints;
using AssetHub.Middleware;
using Dam.Application.Services;
using Dam.Ui;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;
using Hangfire.Dashboard;
using Serilog;

namespace AssetHub.Extensions;

/// <summary>
/// Extension methods for configuring the WebApplication middleware pipeline,
/// startup tasks, and endpoint mapping.
/// </summary>
public static class WebApplicationExtensions
{
    // ── Startup tasks ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs database migration and ensures the MinIO bucket exists.
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
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

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
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
    }

    // ── Endpoint mapping ────────────────────────────────────────────────────

    /// <summary>
    /// Maps all API endpoints, auth routes, Blazor, Hangfire, and health checks.
    /// </summary>
    public static void MapAssetHubEndpoints(this WebApplication app)
    {
        // Build stamp
        app.MapGet("/__build", () =>
            Results.Json(new
            {
                stamp = Dam.Application.BuildInfo.Stamp,
                environment = app.Environment.EnvironmentName
            }));

        // OIDC callback fallback
        app.MapMethods("/signin-oidc", new[] { "GET", "POST" }, () =>
                Results.BadRequest("OIDC callback hit without state/code. Start login via /auth/login."))
            .AllowAnonymous();

        // Auth routes
        app.MapGet("/auth/login", async (HttpContext http, string? returnUrl) =>
        {
            // Prevent open redirect: only allow local (relative) return URLs
            var redirectUri = "/";
            if (!string.IsNullOrWhiteSpace(returnUrl)
                && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
                && !returnUrl.StartsWith("//"))
            {
                redirectUri = returnUrl;
            }
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
        app.MapCollectionEndpoints();
        app.MapAssetEndpoints();
        app.MapShareEndpoints();
        app.MapAdminEndpoints();

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

        // Health checks
        MapHealthCheckEndpoints(app);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static async Task MigrateDatabaseAsync(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
    {
        try
        {
            var db = services.GetRequiredService<Dam.Infrastructure.Data.AssetHubDbContext>();
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
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
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
            Dam.Application.BuildInfo.Stamp, app.Environment.EnvironmentName);
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
                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                }
            }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException badEx) when (context.Request.Path.StartsWithSegments("/api"))
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandler");
                logger.LogWarning(badEx, "Bad request on {Method} {Path}",
                    context.Request.Method, context.Request.Path);

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                        title = "Bad request",
                        status = 400,
                        detail = "The request was invalid. Please try again."
                    });
                }
            }
            catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandler");
                logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                    context.Request.Method, context.Request.Path);

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                        title = "An unexpected error occurred",
                        status = 500,
                        detail = "Something went wrong on the server. Please try again or contact support."
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
                description = e.Value.Description,
                error = e.Value.Exception?.Message
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
