using AssetHub.Api.HealthChecks;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.DependencyInjection;
using AssetHub.Infrastructure.Services;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using MudBlazor.Services;
using Polly;
using System.Threading.RateLimiting;

namespace AssetHub.Api.Extensions;

/// <summary>
/// Registers all application services, infrastructure, and third-party integrations
/// with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string ReadyTag = ReadyTag;

    public static IServiceCollection AddAssetHubServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ConfigureWebHostBuilder webHost)
    {
        // ── Kestrel limits ──────────────────────────────────────────────────
        var maxUploadMb = configuration.GetValue("App:MaxUploadSizeMb", Constants.Limits.DefaultMaxUploadSizeMb);
        webHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = (long)maxUploadMb * 1024 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
        });

        // ── HSTS (production hardening) ────────────────────────────────────
        if (!environment.IsDevelopment())
        {
            services.AddHsts(options =>
            {
                options.MaxAge = TimeSpan.FromDays(365);
                options.IncludeSubDomains = true;
                options.Preload = true;
            });
        }

        // ── Localization (Swedish & English) ────────────────────────────────
        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[] { "en", "sv" };
            options.SetDefaultCulture("en")
                .AddSupportedCultures(supportedCultures)
                .AddSupportedUICultures(supportedCultures);
            options.RequestCultureProviders.Insert(0,
                new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider());
        });

        // ── Blazor Server ───────────────────────────────────────────────────
        services.AddRazorComponents()
                .AddInteractiveServerComponents();

        services.AddHttpContextAccessor();

        // ── Data Protection ─────────────────────────────────────────────────
        services.AddDataProtection()
            .PersistKeysToDbContext<AssetHubDbContext>()
            .SetApplicationName("AssetHub");

        // ── Shared infrastructure: DB, Hangfire storage, MinIO, Repos, core services
        services.AddSharedInfrastructure(configuration);

        // ── Hangfire server (API host processes jobs with constrained workers) ───
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = Math.Max(Constants.Limits.ApiMinHangfireWorkers, Math.Min(Environment.ProcessorCount, Constants.Limits.ApiMaxHangfireWorkers));
        });

        // ── Rate Limiting ───────────────────────────────────────────────────
        ConfigureRateLimiting(services);

        // ── MudBlazor ───────────────────────────────────────────────────────
        services.AddMudServices();

        // ── Caching ─────────────────────────────────────────────────────────
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = Constants.Limits.MemoryCacheSizeLimit;
        });

        // ── Options (API-specific — shared options are in AddSharedInfrastructure) ─
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));

        services.AddOptions<KeycloakSettings>()
            .Bind(configuration.GetSection(KeycloakSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AppSettings>()
            .Bind(configuration.GetSection(AppSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Application & Domain Services (API-only) ────────────────────────
        services.AddScoped<ICollectionAuthorizationService, CollectionAuthorizationService>();
        services.AddScoped<IUserLookupService, UserLookupService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IUserSyncService, UserSyncService>();
        // IZipBuildService registered in AddSharedInfrastructure for Worker access
        services.AddScoped<IShareService, ShareService>();
        services.AddScoped<IUserCleanupService, UserCleanupService>();
        services.AddSingleton<IMalwareScannerService, ClamAvScannerService>();

        // ── Orchestration Services (business logic layer) ───────────────────
        services.AddScoped<CurrentUser>(sp =>
        {
            var hca = sp.GetRequiredService<IHttpContextAccessor>();
            var user = hca.HttpContext?.User;
            var userId = user?.GetUserId();
            if (userId != null)
                return new CurrentUser(userId, user!.IsGlobalAdmin());
            
            // Log when returning anonymous user (background jobs, missing auth, etc.)
            var logger = sp.GetService<ILogger<CurrentUser>>();
            logger?.LogDebug("CurrentUser resolved without HttpContext — returning anonymous user");
            return CurrentUser.Anonymous;
        });
        
        // Asset services split by responsibility (Interface Segregation Principle)
        services.AddScoped<IAssetService, AssetService>();           // Commands: update, delete, collection membership
        services.AddScoped<IAssetQueryService, AssetQueryService>(); // Queries: get, list, rendition URLs
        services.AddScoped<IAssetUploadService, AssetUploadService>(); // Uploads: streaming and presigned
        
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<CollectionAclService>();
        services.AddScoped<ICollectionAclService>(sp => sp.GetRequiredService<CollectionAclService>());
        services.AddScoped<IAdminCollectionAclService>(sp => sp.GetRequiredService<CollectionAclService>());
        
        // Admin services split by responsibility (Interface Segregation Principle)
        services.AddScoped<IShareAdminService, ShareAdminService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<IUserAdminQueryService>(sp => sp.GetRequiredService<UserAdminService>());
        services.AddScoped<IUserAdminService>(sp => sp.GetRequiredService<UserAdminService>());
        
        services.AddScoped<ShareAccessDependencies>();
        services.AddScoped<ShareAccessService>();
        services.AddScoped<IPublicShareAccessService>(sp => sp.GetRequiredService<ShareAccessService>());
        services.AddScoped<IAuthenticatedShareAccessService>(sp => sp.GetRequiredService<ShareAccessService>());
        services.AddScoped<IDashboardQueryService, DashboardQueryService>();
        services.AddScoped<IDashboardService, DashboardService>();

        // ── Keycloak Admin API HttpClient ───────────────────────────────────
        // HttpClient.Timeout is disabled (Infinite) because Polly manages both
        // per-attempt and total timeouts. Setting HttpClient.Timeout alongside
        // Polly would cap the entire retry sequence, potentially killing the
        // last retry mid-flight.
        var keycloakAttemptTimeoutSeconds = configuration.GetValue("Keycloak:TimeoutSeconds", 10);
        services.AddHttpClient<IKeycloakUserService, KeycloakUserService>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .ConfigurePrimaryHttpMessageHandler(() => CreateHttpHandler(environment))
        .AddResilienceHandler("keycloak", builder =>
        {
            // Retry 5xx and transient network errors; never retry 4xx (auth/conflict).
            static bool IsTransientHttpFailure(Outcome<HttpResponseMessage> outcome) =>
                outcome.Exception is HttpRequestException or TimeoutException
                || (outcome.Result is { StatusCode: >= System.Net.HttpStatusCode.InternalServerError });

            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = args => ValueTask.FromResult(IsTransientHttpFailure(args.Outcome))
            });

            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                BreakDuration = TimeSpan.FromSeconds(15),
                ShouldHandle = args => ValueTask.FromResult(IsTransientHttpFailure(args.Outcome))
            });

            // Per-attempt timeout (each individual HTTP call).
            // Total time is bounded by: attempts × timeout + backoff delays.
            builder.AddTimeout(TimeSpan.FromSeconds(keycloakAttemptTimeoutSeconds));
        });

        // ── UI Services ─────────────────────────────────────────────────────
        services.AddScoped<AssetHub.Ui.Services.IUserFeedbackService, AssetHub.Ui.Services.UserFeedbackService>();
        services.AddScoped<AssetHub.Ui.Services.IClipboardService, AssetHub.Ui.Services.ClipboardService>();
        services.AddScoped<AssetHub.Ui.Services.LocalStorageService>();
        services.AddTransient<AssetHub.Ui.Services.CookieForwardingHandler>();

        var connectionString = configuration.GetConnectionString("Postgres");

        services.AddHttpClient<AssetHub.Ui.Services.AssetHubApiClient>((sp, client) =>
        {
            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            var request = httpContextAccessor.HttpContext?.Request;
            if (request != null)
            {
                client.BaseAddress = new Uri($"{request.Scheme}://{request.Host}");
            }
            else
            {
                var baseUrl = configuration["App:BaseUrl"];
                if (string.IsNullOrWhiteSpace(baseUrl))
                    throw new InvalidOperationException(
                        "App:BaseUrl is required when HttpContext is not available. " +
                        "Check appsettings for the current environment.");
                client.BaseAddress = new Uri(baseUrl);
            }
        })
        .ConfigurePrimaryHttpMessageHandler(() => CreateHttpHandler(environment))
        .AddHttpMessageHandler<AssetHub.Ui.Services.CookieForwardingHandler>();

        // ── Health checks ───────────────────────────────────────────────────
        services.AddHealthChecks()
            .AddNpgSql(
                connectionString ?? throw new InvalidOperationException("ConnectionStrings:Postgres required"),
                name: "postgresql",
                tags: ["db", ReadyTag])
            .AddCheck<MinioHealthCheck>("minio", tags: ["storage", ReadyTag])
            .AddCheck<KeycloakHealthCheck>("keycloak", tags: ["auth", ReadyTag])
            .AddCheck<ClamAvHealthCheck>("clamav", tags: ["security", ReadyTag]);

        return services;
    }

    /// <summary>
    /// Configures rate limiting with global per-user/per-IP partitioning
    /// and named policies for Blazor SignalR, share endpoints, and password attempts.
    /// </summary>
    private static void ConfigureRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "text/plain";
                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please try again later.", cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(GetGlobalRateLimitPartition);

            options.AddPolicy(Constants.RateLimitPolicies.BlazorSignalR, context =>
                IpSlidingWindowPartition(context, "blazor", permitLimit: 60, window: TimeSpan.FromMinutes(1)));

            options.AddPolicy(Constants.RateLimitPolicies.ShareAnonymous, context =>
                IpSlidingWindowPartition(context, "share", permitLimit: 30, window: TimeSpan.FromMinutes(1)));

            options.AddPolicy(Constants.RateLimitPolicies.SharePassword, context =>
                IpSlidingWindowPartition(context, "sharepw", permitLimit: 10, window: TimeSpan.FromMinutes(5), segments: 5));
        });
    }

    private static RateLimitPartition<string> GetGlobalRateLimitPartition(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var anonIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
            return SlidingWindowPartition($"anon_{anonIp}", permitLimit: 100, window: TimeSpan.FromMinutes(1));
        }

        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null)
        {
            var fallbackIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
            return SlidingWindowPartition($"nosubject_{fallbackIp}", permitLimit: 50, window: TimeSpan.FromMinutes(1));
        }

        return SlidingWindowPartition(userId, permitLimit: 200, window: TimeSpan.FromMinutes(1));
    }

    private static RateLimitPartition<string> IpSlidingWindowPartition(
        HttpContext context, string prefix, int permitLimit, TimeSpan window, int segments = 6)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        return SlidingWindowPartition($"{prefix}_{ip}", permitLimit, window, segments);
    }

    private static RateLimitPartition<string> SlidingWindowPartition(
        string key, int permitLimit, TimeSpan window, int segments = 6) =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            SegmentsPerWindow = segments,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });

    /// <summary>
    /// Creates an HttpClientHandler that bypasses TLS certificate validation in
    /// Development only (self-signed certs for Keycloak, etc.). In all other
    /// environments, standard certificate validation is enforced.
    /// </summary>
    private static HttpClientHandler CreateHttpHandler(IWebHostEnvironment environment)
    {
        var handler = new HttpClientHandler();
        if (environment.IsDevelopment())
        {
            // Development only: self-signed certs for local Keycloak/MinIO
#pragma warning disable S4830 // Server certificates should be verified during SSL/TLS connections
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#pragma warning restore S4830
        }
        return handler;
    }
}
