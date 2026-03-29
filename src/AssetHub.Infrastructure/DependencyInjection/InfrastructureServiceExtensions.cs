using System.Net.Sockets;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Resilience;
using Minio;
using Minio.Exceptions;
using Polly;
using Polly.Registry;

namespace AssetHub.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services shared by both the API host and the Worker host:
/// database, Hangfire, MinIO, repositories, and core domain services.
/// Host-specific services (auth, Blazor, health checks, etc.) stay in each host project.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Adds all shared infrastructure services to the DI container.
    /// Registers Hangfire storage but NOT the Hangfire server — each host
    /// (API, Worker) should call AddHangfireServer() with its own options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddSharedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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

        services.AddDbContext<AssetHubDbContext>(options =>
        {
            options.UseNpgsql(dataSource);
            options.ConfigureWarnings(w =>
                w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        services.AddDbContextFactory<AssetHubDbContext>(options =>
        {
            options.UseNpgsql(dataSource);
            options.ConfigureWarnings(w =>
                w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }, ServiceLifetime.Scoped);

        // ── Hangfire ────────────────────────────────────────────────────────
        var hangfireConnectionString = configuration["Hangfire:ConnectionString"];
        if (string.IsNullOrWhiteSpace(hangfireConnectionString))
            hangfireConnectionString = connectionString;

        services.AddHangfire(config =>
        {
            config.UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(hangfireConnectionString));
        });

        // NOTE: Each host should call AddHangfireServer() with its own options.
        // Both the API and Worker configure queues including "media-processing".

        // ── Options ─────────────────────────────────────────────────────────
        services.AddOptions<MinIOSettings>()
            .Bind(configuration.GetSection(MinIOSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<ImageProcessingSettings>(configuration.GetSection(ImageProcessingSettings.SectionName));

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
        // Register concrete types for Hangfire job resolution, then forward the interface
        services.AddScoped<ImageMetadataExtractor>();
        services.AddScoped<ImageProcessingService>();
        services.AddScoped<VideoProcessingService>();
        services.AddScoped<MediaProcessingService>();
        services.AddScoped<IMediaProcessingService>(sp => sp.GetRequiredService<MediaProcessingService>());
        services.AddScoped<ZipBuildDataDependencies>();
        services.AddScoped<ZipBuildService>();
        services.AddScoped<IZipBuildService>(sp => sp.GetRequiredService<ZipBuildService>());
        services.AddScoped<IAssetDeletionService, AssetDeletionService>();

        return services;
    }

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
