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
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Policy for anonymous share endpoints (brute-force protection)
            options.AddPolicy("ShareAnonymous", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Stricter policy for share password attempts
            options.AddPolicy("SharePassword", context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        SegmentsPerWindow = 5,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

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
        services.AddScoped<IZipBuildService, ZipBuildService>();
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
        services.AddScoped<ICollectionAclService, CollectionAclService>();
        
        // Admin services split by responsibility (Interface Segregation Principle)
        services.AddScoped<IShareAdminService, ShareAdminService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IAdminService, AdminService>(); // Kept for backward compatibility
        
        services.AddScoped<IShareAccessService, ShareAccessService>();
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
                tags: ["db", "ready"])
            .AddCheck<MinioHealthCheck>("minio", tags: ["storage", "ready"])
            .AddCheck<KeycloakHealthCheck>("keycloak", tags: ["auth", "ready"])
            .AddCheck<ClamAvHealthCheck>("clamav", tags: ["security", "ready"]);

        return services;
    }

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
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    }
}
