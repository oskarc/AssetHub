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
using Microsoft.Extensions.Caching.Memory;
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
        // The API host uses default options; the Worker host configures queues/worker count.

        // ── Options ─────────────────────────────────────────────────────────
        services.AddOptions<MinIOSettings>()
            .Bind(configuration.GetSection(MinIOSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<ImageProcessingSettings>(configuration.GetSection(ImageProcessingSettings.SectionName));

        // ── MinIO clients ───────────────────────────────────────────────────
        AddMinIOClients(services, configuration);

        // ── Repositories ────────────────────────────────────────────────────
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<ICollectionAclRepository, CollectionAclRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAssetCollectionRepository, AssetCollectionRepository>();
        services.AddScoped<IShareRepository, ShareRepository>();

        // ── Resilience pipelines ──────────────────────────────────────────
        AddResiliencePipelines(services);

        // ── Core services ───────────────────────────────────────────────────
        services.AddScoped<IMinIOAdapter>(sp =>
        {
            var internalClient = sp.GetRequiredService<IMinioClient>();
            var publicClient = sp.GetRequiredKeyedService<IMinioClient>("public");
            var adapterLogger = sp.GetRequiredService<ILogger<MinIOAdapter>>();
            var cache = sp.GetRequiredService<IMemoryCache>();
            var pipelineProvider = sp.GetRequiredService<ResiliencePipelineProvider<string>>();
            return new MinIOAdapter(internalClient, publicClient, adapterLogger, cache, pipelineProvider);
        });
        services.AddScoped<IMediaProcessingService, MediaProcessingService>();
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
            builder.AddRetryWithCircuitBreaker(minioShouldHandle,
                retryAttempts: 3, retryDelay: TimeSpan.FromSeconds(1), retryBackoff: DelayBackoffType.Exponential,
                breakDuration: TimeSpan.FromSeconds(30), samplingDuration: TimeSpan.FromSeconds(30), minimumThroughput: 5);
        });

        // ClamAV: lighter retry for TCP socket connections
        var clamavShouldHandle = new PredicateBuilder()
            .Handle<SocketException>();

        services.AddResiliencePipeline("clamav", builder =>
        {
            builder.AddRetryWithCircuitBreaker(clamavShouldHandle,
                retryAttempts: 2, retryDelay: TimeSpan.FromMilliseconds(500), retryBackoff: DelayBackoffType.Constant,
                breakDuration: TimeSpan.FromSeconds(60), samplingDuration: TimeSpan.FromSeconds(60), minimumThroughput: 3);
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
        int retryAttempts,
        TimeSpan retryDelay,
        DelayBackoffType retryBackoff,
        TimeSpan breakDuration,
        TimeSpan samplingDuration,
        int minimumThroughput,
        double failureRatio = 0.5)
    {
        builder.AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            MaxRetryAttempts = retryAttempts,
            BackoffType = retryBackoff,
            Delay = retryDelay,
            ShouldHandle = shouldHandle
        });
        builder.AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
        {
            SamplingDuration = samplingDuration,
            FailureRatio = failureRatio,
            MinimumThroughput = minimumThroughput,
            BreakDuration = breakDuration,
            ShouldHandle = shouldHandle
        });
    }

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

        // Public client for presigned URLs that browsers access directly
        var publicEndpoint = minioConfig["PublicUrl"];
        var publicUseSsl = minioConfig.GetValue("PublicUseSSL", minioUseSsl);
        IMinioClient publicMinioClient;
        if (!string.IsNullOrWhiteSpace(publicEndpoint) && publicEndpoint != minioEndpoint)
        {
            publicMinioClient = new MinioClient()
                .WithEndpoint(publicEndpoint)
                .WithCredentials(minioAccessKey, minioSecretKey)
                .WithSSL(publicUseSsl)
                .Build();
        }
        else
        {
            publicMinioClient = minioClient;
        }

        services.AddKeyedSingleton<IMinioClient>("public", publicMinioClient);
    }
}
