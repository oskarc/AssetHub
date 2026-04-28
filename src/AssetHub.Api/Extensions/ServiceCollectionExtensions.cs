using AssetHub.Api.BackgroundServices;
using AssetHub.Api.HealthChecks;
using AssetHub.Api.OpenApi;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.DependencyInjection;
using AssetHub.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Polly;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

namespace AssetHub.Api.Extensions;

/// <summary>
/// Registers all application services, infrastructure, and third-party integrations
/// with the dependency injection container.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S1200:Classes should not be coupled to too many other classes",
    Justification = "DI composition root — couples to every service it registers. That is its single responsibility.")]
public static class ServiceCollectionExtensions
{
    private const string ReadyTag = "ready";

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell", "S138:Functions should not have too many lines of code",
        Justification = "DI registration is one long bootstrap chain; splitting just spreads the same coupling across N partial methods.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell", "S1541:Methods and properties should not be too complex",
        Justification = "Cyclomatic complexity comes from environment-conditional registrations (test/dev/prod-only services).")]
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

        // ── Response compression ────────────────────────────────────────────
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                ["application/json", "text/plain", "application/javascript", "text/css"]);
        });

        // ── JSON serialization ──────────────────────────────────────────────
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
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

        // ── SignalR Redis backplane (multi-instance Blazor Server) ───────────
        var redisConnection = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrEmpty(redisConnection))
        {
            services.AddSignalR()
                .AddStackExchangeRedis(redisConnection, options =>
                {
                    options.Configuration.ChannelPrefix =
                        StackExchange.Redis.RedisChannel.Literal("AssetHub:");
                });
        }

        services.AddHttpContextAccessor();

        // ── Antiforgery (P-12 / A-7) ────────────────────────────────────────
        // Custom cookie + header names so the X-CSRF-TOKEN convention is
        // explicit (default is __RequestVerificationToken which clashes with
        // Razor Pages tooling). In Production the cookie name uses the real
        // RFC 6265bis "__Host-" prefix (hyphen, not dot) so browsers enforce
        // the Secure flag, Path=/ and no Domain attribute. In Development the
        // cookie is not Secure under HTTP, so the prefix is dropped — browsers
        // reject "__Host-" cookies that are not Secure.
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = environment.IsDevelopment()
                ? "assethub.csrf"
                : "__Host-assethub.csrf";
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Blazor Server reads the token server-side via IAntiforgery —
            // browser JS never needs the cookie, so lock it down.
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.HeaderName = "X-CSRF-TOKEN";
        });

        // ── Shared infrastructure: DB, MinIO, Repos, core services ─────────
        services.AddSharedInfrastructure(configuration, environment);

        // ── Data Protection (shared with Worker via AddAssetHubDataProtection)
        // Must come AFTER AddSharedInfrastructure so the AssetHubDbContext
        // is registered before PersistKeysToDbContext binds to it.
        services.AddAssetHubDataProtection(configuration, environment);

        // ── RabbitMQ settings (used by Wolverine, configured in Program.cs) ───
        services.AddOptions<RabbitMQSettings>()
            .BindConfiguration(RabbitMQSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Background services (recurring tasks) ────────────────────────────
        services.AddHostedService<UserSyncBackgroundService>();
        services.AddHostedService<ZipCleanupBackgroundService>();

        // ── Rate Limiting ───────────────────────────────────────────────────
        ConfigureRateLimiting(services, environment);

        // ── MudBlazor ───────────────────────────────────────────────────────
        services.AddMudServices();

        // ── OpenAPI (public contract) ───────────────────────────────────────
        // The "v1" document only includes endpoints marked with [PublicApi]; admin
        // and internal endpoints remain functional but undocumented in the public schema.
        services.AddOpenApi("v1", options =>
        {
            options.ShouldInclude = description =>
                description.ActionDescriptor.EndpointMetadata.OfType<PublicApiAttribute>().Any();

            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "AssetHub Public API";
                document.Info.Version = "v1";
                document.Info.Description =
                    "Stable REST surface for AssetHub integrations. Endpoints listed here " +
                    "promise SemVer compatibility; admin-only and internal endpoints are " +
                    "intentionally excluded and may change without notice.";
                return Task.CompletedTask;
            });

            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddSchemaTransformer<DictionaryOfObjectSchemaTransformer>();
        });

        // ── Caching ─────────────────────────────────────────────────────────
        // HybridCache (L1 in-memory + L2 Redis) is registered via AddSharedInfrastructure.
        // No standalone AddMemoryCache needed — HybridCache manages its own L1 cache.

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
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IUserSyncService, UserSyncService>();
        // IZipBuildService registered in AddSharedInfrastructure for Worker access
        services.AddScoped<ShareServiceRepositories>();
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
        services.AddScoped<AssetQueryRepositories>();
        services.AddScoped<AssetUploadRepositories>();
        services.AddScoped<AssetUploadPipeline>();
        services.AddScoped<AssetServiceRepositories>();
        services.AddScoped<IAssetService, AssetService>();           // Commands: update, delete, collection membership
        services.AddScoped<IAssetQueryService, AssetQueryService>(); // Queries: get, list, rendition URLs
        services.AddScoped<IAssetUploadService, AssetUploadService>(); // Uploads: streaming and presigned

        // Image editing services
        services.AddScoped<IExportPresetService, ExportPresetService>();
        services.AddScoped<IExportPresetQueryService, ExportPresetQueryService>();
        services.AddScoped<IImageEditingService, ImageEditingService>();

        // Metadata schemas and taxonomies
        services.AddScoped<IMetadataSchemaService, MetadataSchemaService>();
        services.AddScoped<IMetadataSchemaQueryService, MetadataSchemaQueryService>();
        services.AddScoped<ITaxonomyService, TaxonomyService>();
        services.AddScoped<ITaxonomyQueryService, TaxonomyQueryService>();
        services.AddScoped<IAssetMetadataService, AssetMetadataService>();

        // Search
        services.AddScoped<IAssetSearchService, AssetSearchService>();
        services.AddScoped<ISavedSearchService, SavedSearchService>();

        // Trash (T1-LIFE-01)
        services.AddScoped<IAssetTrashService, AssetTrashService>();

        // Versioning (T1-VER-01)
        services.AddScoped<IAssetVersionService, AssetVersionService>();

        // Asset comments (T3-COL-01)
        services.AddScoped<IAssetCommentService, AssetCommentService>();

        // Asset workflow (T3-WF-01)
        services.AddScoped<IAssetWorkflowService, AssetWorkflowService>();

        // Webhooks (T3-INT-01)
        services.AddScoped<IWebhookService, WebhookService>();

        // Brands (T4-BP-01)
        services.AddScoped<IBrandService, BrandService>();

        // On-the-fly renditions (T3-REND-01)
        services.AddScoped<IRenditionImageResizer, ImageProcessingRenditionResizer>();
        services.AddScoped<IRenditionService, RenditionService>();

        // Guest invitations (T4-GUEST-01)
        services.AddScoped<IGuestInvitationService, GuestInvitationService>();

        // Personal access tokens (T1-API-01)
        services.AddScoped<IPersonalAccessTokenService, PersonalAccessTokenService>();

        // Collection services split by responsibility (Interface Segregation Principle)
        services.AddScoped<CollectionServiceRepositories>();
        services.AddScoped<ICollectionQueryService, CollectionQueryService>(); // Queries: list, get by ID
        services.AddScoped<ICollectionService, CollectionService>();           // Commands: create, update, delete, download
        services.AddScoped<CollectionAdminRepositories>();
        services.AddScoped<ICollectionAdminService, CollectionAdminService>(); // Admin: bulk delete, bulk set access
        services.AddScoped<CollectionAclRepositories>();
        services.AddScoped<CollectionAclService>();
        services.AddScoped<ICollectionAclService>(sp => sp.GetRequiredService<CollectionAclService>());
        services.AddScoped<IAdminCollectionAclService>(sp => sp.GetRequiredService<CollectionAclService>());
        
        // Admin services split by responsibility (Interface Segregation Principle)
        services.AddScoped<IShareAdminService, ShareAdminService>();
        services.AddScoped<UserLifecycleServices>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<IUserAdminQueryService>(sp => sp.GetRequiredService<UserAdminService>());
        services.AddScoped<IUserAdminService>(sp => sp.GetRequiredService<UserAdminService>());
        
        services.AddScoped<IPublicShareAccessService, PublicShareAccessService>();
        services.AddScoped<IAuthenticatedShareAccessService, AuthenticatedShareAccessService>();
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
        services.AddScoped<AssetHub.Ui.Services.ThemeService>();
        services.AddScoped<AssetHub.Ui.Services.LocalizedDisplayService>();

        // AssetHubApiClient is an in-process facade over Application services
        // (no HTTP loopback). Registered as scoped so it shares the request
        // scope with CurrentUser, the DbContext, and the underlying services.
        services.AddScoped<AssetHub.Ui.Services.AssetHubApiClient>();

        var connectionString = configuration.GetConnectionString("Postgres");

        // ── Health checks ───────────────────────────────────────────────────
        var healthChecks = services.AddHealthChecks()
            .AddNpgSql(
                connectionString ?? throw new InvalidOperationException("ConnectionStrings:Postgres required"),
                name: "postgresql",
                tags: ["db", ReadyTag])
            .AddCheck<MinioHealthCheck>("minio", tags: ["storage", ReadyTag])
            .AddCheck<KeycloakHealthCheck>("keycloak", tags: ["auth", ReadyTag])
            .AddCheck<ClamAvHealthCheck>("clamav", tags: ["security", ReadyTag]);

        if (!string.IsNullOrEmpty(redisConnection))
        {
            healthChecks.AddRedis(
                redisConnection,
                name: "redis",
                tags: ["cache", ReadyTag]);
        }

        return services;
    }

    /// <summary>
    /// Configures rate limiting with global per-user/per-IP partitioning
    /// and named policies for Blazor SignalR, share endpoints, and password attempts.
    /// </summary>
    private static void ConfigureRateLimiting(IServiceCollection services, IWebHostEnvironment environment)
    {
        // Development: relax limits for E2E/manual testing (Blazor pages generate many requests per navigation)
        var isDev = environment.IsDevelopment();

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

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                isDev ? GetDevRateLimitPartition : GetGlobalRateLimitPartition);

            options.AddPolicy(Constants.RateLimitPolicies.BlazorSignalR, context =>
                IpSlidingWindowPartition(context, "blazor",
                    permitLimit: isDev ? 600 : 60, window: TimeSpan.FromMinutes(1)));

            options.AddPolicy(Constants.RateLimitPolicies.ShareAnonymous, context =>
                IpSlidingWindowPartition(context, "share",
                    permitLimit: isDev ? 300 : 30, window: TimeSpan.FromMinutes(1)));

            options.AddPolicy(Constants.RateLimitPolicies.SharePassword, context =>
                IpSlidingWindowPartition(context, "sharepw", permitLimit: 10, window: TimeSpan.FromMinutes(5), segments: 5));
        });
    }

    private static RateLimitPartition<string> GetDevRateLimitPartition(HttpContext context)
    {
        var key = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? ClientIpPartitionKey(context);
        return SlidingWindowPartition($"dev_{key}", permitLimit: 2000, window: TimeSpan.FromMinutes(1));
    }

    private static RateLimitPartition<string> GetGlobalRateLimitPartition(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var anonIp = ClientIpPartitionKey(context);
            return SlidingWindowPartition($"anon_{anonIp}", permitLimit: 100, window: TimeSpan.FromMinutes(1));
        }

        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null)
        {
            var fallbackIp = ClientIpPartitionKey(context);
            return SlidingWindowPartition($"nosubject_{fallbackIp}", permitLimit: 50, window: TimeSpan.FromMinutes(1));
        }

        return SlidingWindowPartition(userId, permitLimit: 200, window: TimeSpan.FromMinutes(1));
    }

    private static RateLimitPartition<string> IpSlidingWindowPartition(
        HttpContext context, string prefix, int permitLimit, TimeSpan window, int segments = 6)
    {
        var ip = ClientIpPartitionKey(context);
        return SlidingWindowPartition($"{prefix}_{ip}", permitLimit, window, segments);
    }

    /// <summary>
    /// Builds a stable rate-limit partition key from the connection's remote
    /// IP, normalising IPv6 down to its /48 prefix. A naïve full-address key
    /// gives an attacker rotating across a single /64 (ISP-allocated to one
    /// customer) effectively unlimited quota; /48 still preserves per-customer
    /// fairness (most ISPs allocate at least a /48 per residential subscriber)
    /// while collapsing the rotation surface (A-8 in the security review).
    /// </summary>
    private static string ClientIpPartitionKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return "unknown-ip";

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            && !address.IsIPv4MappedToIPv6)
        {
            // Truncate IPv6 to /48 (first 6 bytes / 3 hextets).
            var bytes = address.GetAddressBytes();
            // Zero out the trailing 80 bits.
            for (var i = 6; i < bytes.Length; i++) bytes[i] = 0;
            return new System.Net.IPAddress(bytes).ToString() + "/48";
        }
        return address.ToString();
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
