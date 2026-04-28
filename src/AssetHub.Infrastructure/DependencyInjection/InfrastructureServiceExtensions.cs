using System.Net.Sockets;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Resilience;
using Minio;
using Minio.Exceptions;
using Polly;
using Polly.Registry;

namespace AssetHub.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services shared by both the API host and the Worker host:
/// database, MinIO, repositories, and core domain services.
/// Host-specific services (auth, Blazor, health checks, etc.) stay in each host project.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Adds all shared infrastructure services to the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">
    /// Host environment. Used to decide whether <c>PendingModelChangesWarning</c>
    /// throws (non-Development, so a forgotten migration fails CI/prod startup
    /// instead of silently shipping) or logs (Development, keeping iteration
    /// fast). When null, defaults to throwing — safest for libraries / tests.
    /// </param>
    public static IServiceCollection AddSharedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        var pendingModelChangesAction = environment is not null && environment.IsDevelopment()
            ? WarningBehavior.Log
            : WarningBehavior.Throw;
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required");

        // ── Database ────────────────────────────────────────────────────────
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();

        // Enforce connection pool limits to prevent pool exhaustion under load.
        // These can be overridden via the connection string itself.
        var connStringBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        if (connStringBuilder.MaxPoolSize == 100) // 100 = Npgsql default, meaning no explicit override
        {
            connStringBuilder.MaxPoolSize = 50;
            connStringBuilder.Timeout = 15; // seconds to wait for a connection from the pool
            dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = 50;
            dataSourceBuilder.ConnectionStringBuilder.Timeout = 15;
        }

        var dataSource = dataSourceBuilder.Build();
        services.AddSingleton(dataSource);

        // optionsLifetime: Singleton is load-bearing here. Both AddDbContext (below)
        // and AddDbContextFactory (further down) share a single DbContextOptions<>
        // registration. The factory MUST be Singleton (see comment on the factory
        // call), so its options must also be Singleton — otherwise ValidateScopes
        // throws "Cannot consume scoped service DbContextOptions<> from singleton
        // IDbContextFactory<>". The Scoped context registration consumes Singleton
        // options just fine; only the captive-dependency direction is forbidden.
        services.AddDbContext<AssetHubDbContext>(options =>
        {
            options.UseNpgsql(dataSource);
            // Outside Development: throw on missing migrations so CI / prod
            // startup catches the drift instead of silently running with a
            // mismatched model. In Development we keep it as a log warning
            // to avoid blocking iteration before the migration is generated.
            options.ConfigureWarnings(w =>
            {
                if (pendingModelChangesAction == WarningBehavior.Throw)
                    w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
                else
                    w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
            });
        }, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);

        // IDbContextFactory must be Singleton: it's the EF-recommended default,
        // and DbContextProvider (also Singleton) depends on it. Registering the
        // factory as Scoped would create a captive-dependency / scope-validation
        // failure under Development's ValidateScopes=true, and silently "capture"
        // the first request's scope in Production.
        services.AddDbContextFactory<AssetHubDbContext>(options =>
        {
            options.UseNpgsql(dataSource);
            // Outside Development: throw on missing migrations so CI / prod
            // startup catches the drift instead of silently running with a
            // mismatched model. In Development we keep it as a log warning
            // to avoid blocking iteration before the migration is generated.
            options.ConfigureWarnings(w =>
            {
                if (pendingModelChangesAction == WarningBehavior.Throw)
                    w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
                else
                    w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
            });
        });

        // Per-call DbContext lease provider (factory-backed, ambient-aware).
        // Repositories and services use this instead of injecting AssetHubDbContext
        // directly so that concurrent component-driven calls in a Blazor circuit
        // each get their own context and don't trip EF's concurrency detector.
        services.AddSingleton<DbContextProvider>();

        // ── Options ─────────────────────────────────────────────────────────
        services.AddOptions<MinIOSettings>()
            .Bind(configuration.GetSection(MinIOSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<ImageProcessingSettings>(configuration.GetSection(ImageProcessingSettings.SectionName));
        services.Configure<AssetLifecycleSettings>(configuration.GetSection(AssetLifecycleSettings.SectionName));
        services.Configure<WorkflowSettings>(configuration.GetSection(WorkflowSettings.SectionName));
        services.Configure<RenditionSettings>(configuration.GetSection(RenditionSettings.SectionName));

        // ── Caching (Redis L2 + HybridCache L1/L2) ────────────────────────
        var redisConnectionString = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            var redisInstanceName = configuration["Redis:InstanceName"] ?? "AssetHub:";
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = redisInstanceName;
            });
        }
        else
        {
            // Fallback for tests or environments without Redis
            services.AddDistributedMemoryCache();
        }

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(2)
            };
            options.MaximumPayloadBytes = 1024 * 1024; // 1 MB max serialized size
        });

        // ── MinIO clients ───────────────────────────────────────────────────
        AddMinIOClients(services, configuration);

        // ── Repositories ────────────────────────────────────────────────────
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<ICollectionAclRepository, CollectionAclRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAssetCollectionRepository, AssetCollectionRepository>();
        services.AddScoped<IShareRepository, ShareRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        services.AddScoped<IExportPresetRepository, ExportPresetRepository>();
        services.AddScoped<IMigrationRepository, MigrationRepository>();
        services.AddScoped<IMetadataSchemaRepository, MetadataSchemaRepository>();
        services.AddScoped<ITaxonomyRepository, TaxonomyRepository>();
        services.AddScoped<IAssetMetadataRepository, AssetMetadataRepository>();
        services.AddScoped<ISavedSearchRepository, SavedSearchRepository>();
        services.AddScoped<IAssetVersionRepository, AssetVersionRepository>();
        services.AddScoped<IPersonalAccessTokenRepository, PersonalAccessTokenRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationPreferencesRepository, NotificationPreferencesRepository>();
        services.AddScoped<IAssetCommentRepository, AssetCommentRepository>();
        services.AddScoped<IAssetWorkflowTransitionRepository, AssetWorkflowTransitionRepository>();
        services.AddScoped<IWebhookRepository, WebhookRepository>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<IGuestInvitationRepository, GuestInvitationRepository>();
        services.AddScoped<IOrphanedObjectRepository, OrphanedObjectRepository>();

        // ── Resilience pipelines ──────────────────────────────────────────
        AddResiliencePipelines(services);

        // ── Core services ───────────────────────────────────────────────────
        services.AddScoped<IMinIOAdapter>(sp =>
        {
            var internalClient = sp.GetRequiredService<IMinioClient>();
            var publicClient = sp.GetRequiredKeyedService<IMinioClient>("public");
            var adapterLogger = sp.GetRequiredService<ILogger<MinIOAdapter>>();
            var pipelineProvider = sp.GetRequiredService<ResiliencePipelineProvider<string>>();
            var hybridCache = sp.GetRequiredService<HybridCache>();
            return new MinIOAdapter(internalClient, publicClient, adapterLogger, pipelineProvider, hybridCache);
        });
        services.AddScoped<ImageMetadataExtractor>();
        services.AddScoped<ImageProcessingService>();
        services.AddScoped<VideoProcessingService>();
        services.AddScoped<MediaProcessingService>();
        services.AddScoped<IMediaProcessingService>(sp => sp.GetRequiredService<MediaProcessingService>());
        services.AddScoped<ZipBuildDataDependencies>();
        services.AddScoped<ZipBuildService>();
        services.AddScoped<IZipBuildService>(sp => sp.GetRequiredService<ZipBuildService>());
        services.AddScoped<IAssetDeletionService, AssetDeletionService>();
        services.AddScoped<IMigrationService, MigrationService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationPreferencesService, NotificationPreferencesService>();
        services.AddSingleton<INotificationUnsubscribeTokenService, NotificationUnsubscribeTokenService>();
        services.AddSingleton<IMigrationSecretProtector, MigrationSecretProtector>();
        services.AddSingleton<IWebhookSecretProtector, WebhookSecretProtector>();
        services.AddScoped<IWebhookEventPublisher, WebhookEventPublisher>();
        services.AddScoped<IBrandResolver, BrandResolver>();
        services.AddSingleton<IGuestInvitationTokenService, GuestInvitationTokenService>();
        // Migration source connectors: one impl per MigrationSourceType.
        // Registry fans them out by SourceType at resolve time.
        // Scoped (not singleton): CsvMigrationSourceConnector injects the scoped IMinIOAdapter.
        services.AddScoped<IMigrationSourceConnector, CsvMigrationSourceConnector>();
        services.AddScoped<IMigrationSourceConnector, S3MigrationSourceConnector>();
        services.AddScoped<IMigrationSourceConnectorRegistry, MigrationSourceConnectorRegistry>();

        return services;
    }

    /// <summary>
    /// Wires up ASP.NET Core Data Protection. Keys are persisted to the
    /// shared database so all instances of the API + Worker decrypt
    /// consistently. In production they're additionally wrapped with an
    /// X.509 certificate (Docker secret-mounted PFX) so a DB exfil
    /// without the cert yields ciphertext only — defeats the
    /// "everything decrypts from one DB dump" risk in A-1/A-2.
    /// </summary>
    public static IServiceCollection AddAssetHubDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var builder = services.AddDataProtection()
            .PersistKeysToDbContext<AssetHubDbContext>()
            .SetApplicationName("AssetHub");

        var dpSettings = configuration.GetSection(DataProtectionSettings.SectionName)
            .Get<DataProtectionSettings>() ?? new DataProtectionSettings();

        if (!string.IsNullOrWhiteSpace(dpSettings.CertificatePath))
        {
            if (!System.IO.File.Exists(dpSettings.CertificatePath))
            {
                throw new InvalidOperationException(
                    $"DataProtection:CertificatePath '{dpSettings.CertificatePath}' does not exist. " +
                    "Mount the PFX as a Docker secret and ensure the path matches.");
            }
            // Use the .NET 9 X509CertificateLoader — older `new X509Certificate2(path, password)`
            // is obsoleted. Empty/null password = unprotected PFX (dev-only).
            var cert = string.IsNullOrEmpty(dpSettings.CertificatePassword)
                ? System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                    dpSettings.CertificatePath, password: null)
                : System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                    dpSettings.CertificatePath, dpSettings.CertificatePassword);
            builder.ProtectKeysWithCertificate(cert);
        }
        else if (!IsRelaxedEnvironment(environment))
        {
            // In Production / Staging, refuse to start without a wrapping cert.
            // The keyring would otherwise sit in plaintext in the same database
            // it protects (A-1/A-2 in the security review). Development and
            // Testing skip the requirement so unit + integration tests run
            // without a cert mount.
            throw new InvalidOperationException(
                "DataProtection:CertificatePath is required outside of Development / Testing. " +
                "Provide a PFX via Docker secret and configure DataProtection:CertificatePath " +
                "(and CertificatePassword if the PFX has one). Without it the keyring is stored " +
                "in plaintext in the database it protects.");
        }
        // Development / Testing: bare keyring acceptable; persists across restarts via the DB.

        return services;
    }

    private static bool IsRelaxedEnvironment(IHostEnvironment environment)
        => environment.IsDevelopment()
            || environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase);

    private static void AddResiliencePipelines(IServiceCollection services)
    {
        // MinIO: retry transient network/SDK errors with circuit breaker
        var minioShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<SocketException>()
            .Handle<MinioException>(ex => ex is not ObjectNotFoundException and not BucketNotFoundException);

        services.AddResiliencePipeline("minio", builder =>
        {
            builder.AddRetryWithCircuitBreaker(minioShouldHandle, new(
                RetryAttempts: 3, RetryDelay: TimeSpan.FromSeconds(1), RetryBackoff: DelayBackoffType.Exponential,
                BreakDuration: TimeSpan.FromSeconds(30), SamplingDuration: TimeSpan.FromSeconds(30), MinimumThroughput: 5));
        });

        // ClamAV: lighter retry for TCP socket connections
        var clamavShouldHandle = new PredicateBuilder()
            .Handle<SocketException>();

        services.AddResiliencePipeline("clamav", builder =>
        {
            builder.AddRetryWithCircuitBreaker(clamavShouldHandle, new(
                RetryAttempts: 2, RetryDelay: TimeSpan.FromMilliseconds(500), RetryBackoff: DelayBackoffType.Constant,
                BreakDuration: TimeSpan.FromSeconds(60), SamplingDuration: TimeSpan.FromSeconds(60), MinimumThroughput: 3));
        });

        // SMTP: retry transient email failures (no circuit breaker — low volume)
        var smtpShouldHandle = new PredicateBuilder()
            .Handle<System.Net.Mail.SmtpException>()
            .Handle<SocketException>();

        services.AddResiliencePipeline("smtp", builder =>
        {
            builder.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = smtpShouldHandle
            });
        });
    }

    /// <summary>
    /// Adds a retry strategy followed by a circuit breaker, both using the same
    /// <paramref name="shouldHandle"/> predicate. This avoids duplicating the
    /// predicate across the two strategies.
    /// </summary>
    private static void AddRetryWithCircuitBreaker(
        this ResiliencePipelineBuilder builder,
        PredicateBuilder<object> shouldHandle,
        RetryWithCircuitBreakerOptions opts)
    {
        builder.AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            MaxRetryAttempts = opts.RetryAttempts,
            BackoffType = opts.RetryBackoff,
            Delay = opts.RetryDelay,
            ShouldHandle = shouldHandle
        });
        builder.AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
        {
            SamplingDuration = opts.SamplingDuration,
            FailureRatio = opts.FailureRatio,
            MinimumThroughput = opts.MinimumThroughput,
            BreakDuration = opts.BreakDuration,
            ShouldHandle = shouldHandle
        });
    }

    private sealed record RetryWithCircuitBreakerOptions(
        int RetryAttempts,
        TimeSpan RetryDelay,
        DelayBackoffType RetryBackoff,
        TimeSpan BreakDuration,
        TimeSpan SamplingDuration,
        int MinimumThroughput,
        double FailureRatio = 0.5);

    private static void AddMinIOClients(IServiceCollection services, IConfiguration configuration)
    {
        var minioConfig = configuration.GetSection("MinIO");
        var minioEndpoint = minioConfig["Endpoint"]
            ?? throw new InvalidOperationException("MinIO:Endpoint is required.");
        var minioAccessKey = minioConfig["AccessKey"]
            ?? throw new InvalidOperationException("MinIO:AccessKey is required.");
        var minioSecretKey = minioConfig["SecretKey"]
            ?? throw new InvalidOperationException("MinIO:SecretKey is required.");
        var minioUseSsl = minioConfig.GetValue("UseSSL", true);

        var minioClient = new MinioClient()
            .WithEndpoint(minioEndpoint)
            .WithCredentials(minioAccessKey, minioSecretKey)
            .WithSSL(minioUseSsl)
            .Build();

        services.AddSingleton<IMinioClient>(minioClient);

        // Public client for presigned URLs that browsers access directly.
        // The region is set explicitly so the SDK doesn't make an HTTP call to the
        // public endpoint (which may be unreachable from inside Docker) to look up
        // the bucket region.
        var publicEndpoint = minioConfig["PublicUrl"];
        var publicUseSsl = minioConfig.GetValue("PublicUseSSL", minioUseSsl);
        var minioRegion = minioConfig["Region"] ?? "us-east-1";
        IMinioClient publicMinioClient;
        if (!string.IsNullOrWhiteSpace(publicEndpoint) && publicEndpoint != minioEndpoint)
        {
            publicMinioClient = new MinioClient()
                .WithEndpoint(publicEndpoint)
                .WithCredentials(minioAccessKey, minioSecretKey)
                .WithSSL(publicUseSsl)
                .WithRegion(minioRegion)
                .Build();
        }
        else
        {
            publicMinioClient = minioClient;
        }

        services.AddKeyedSingleton<IMinioClient>("public", publicMinioClient);
    }
}
