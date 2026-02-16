using AssetHub.HealthChecks;
using Dam.Application;
using Dam.Application.Configuration;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Infrastructure.Data;
using Dam.Infrastructure.Repositories;
using Dam.Infrastructure.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;
using MudBlazor.Services;

namespace AssetHub.Extensions;

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
        });

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

        // ── Database ────────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("Postgres");
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<AssetHubDbContext>(options =>
        {
            options.UseNpgsql(dataSource);
            // Suppress EF Core 9's PendingModelChangesWarning so existing migrations can apply
            // even when there are minor model snapshot differences (e.g. value comparer warnings).
            options.ConfigureWarnings(w =>
                w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        // ── Hangfire ────────────────────────────────────────────────────────
        var hangfireConnectionString = configuration["Hangfire:ConnectionString"];
        if (string.IsNullOrWhiteSpace(hangfireConnectionString))
            hangfireConnectionString = configuration.GetConnectionString("Postgres") ?? "";

        services.AddHangfire(config =>
        {
            config.UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(hangfireConnectionString));
        });
        services.AddHangfireServer();

        // ── MudBlazor ───────────────────────────────────────────────────────
        services.AddMudServices();

        // ── Caching ─────────────────────────────────────────────────────────
        services.AddMemoryCache();

        // ── Options ─────────────────────────────────────────────────────────
        services.Configure<MinIOSettings>(configuration.GetSection(MinIOSettings.SectionName));
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.Configure<KeycloakSettings>(configuration.GetSection(KeycloakSettings.SectionName));
        services.Configure<AppSettings>(configuration.GetSection(AppSettings.SectionName));
        services.Configure<ImageProcessingSettings>(configuration.GetSection(ImageProcessingSettings.SectionName));

        // ── Application & Domain Services ───────────────────────────────────
        services.AddScoped<ICollectionAuthorizationService, CollectionAuthorizationService>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<ICollectionAclRepository, CollectionAclRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAssetCollectionRepository, AssetCollectionRepository>();
        services.AddScoped<IShareRepository, ShareRepository>();
        services.AddScoped<IMinIOAdapter>(sp =>
        {
            var internalClient = sp.GetRequiredService<Minio.IMinioClient>();
            var publicClient = sp.GetRequiredKeyedService<Minio.IMinioClient>("public");
            var adapterLogger = sp.GetRequiredService<ILogger<MinIOAdapter>>();
            return new MinIOAdapter(internalClient, publicClient, adapterLogger);
        });
        services.AddScoped<IMediaProcessingService, MediaProcessingService>();
        services.AddScoped<IUserLookupService, UserLookupService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserSyncService, UserSyncService>();
        services.AddScoped<IAssetDeletionService, AssetDeletionService>();
        services.AddScoped<IZipDownloadService, ZipDownloadService>();
        services.AddScoped<IShareService, ShareService>();
        services.AddScoped<IUserCleanupService, UserCleanupService>();

        // ── Orchestration Services (business logic layer) ───────────────────
        services.AddScoped<CurrentUser>(sp =>
        {
            var hca = sp.GetRequiredService<IHttpContextAccessor>();
            var user = hca.HttpContext?.User;
            var userId = user?.GetUserId();
            return userId != null
                ? new CurrentUser(userId, user!.IsGlobalAdmin())
                : new CurrentUser("", false);
        });
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<ICollectionAclService, CollectionAclService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IShareAccessService, ShareAccessService>();

        // ── Keycloak Admin API HttpClient ───────────────────────────────────
        var keycloakTimeoutSeconds = configuration.GetValue("Keycloak:TimeoutSeconds", 30);
        services.AddHttpClient<IKeycloakUserService, KeycloakUserService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(keycloakTimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler();
            if (environment.IsDevelopment())
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
            return handler;
        });

        // ── UI Services ─────────────────────────────────────────────────────
        services.AddScoped<Dam.Ui.Services.IUserFeedbackService, Dam.Ui.Services.UserFeedbackService>();
        services.AddTransient<Dam.Ui.Services.CookieForwardingHandler>();

        services.AddHttpClient<Dam.Ui.Services.AssetHubApiClient>((sp, client) =>
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
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler();
            if (environment.IsDevelopment())
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
            return handler;
        })
        .AddHttpMessageHandler<Dam.Ui.Services.CookieForwardingHandler>();

        // ── MinIO clients ───────────────────────────────────────────────────
        AddMinIOClients(services, configuration);

        // ── Health checks ───────────────────────────────────────────────────
        services.AddHealthChecks()
            .AddNpgSql(
                connectionString ?? throw new InvalidOperationException("ConnectionStrings:Postgres required"),
                name: "postgresql",
                tags: ["db", "ready"])
            .AddCheck<MinioHealthCheck>("minio", tags: ["storage", "ready"])
            .AddCheck<KeycloakHealthCheck>("keycloak", tags: ["auth", "ready"]);

        return services;
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

        var minioClient = new Minio.MinioClient()
            .WithEndpoint(minioEndpoint)
            .WithCredentials(minioAccessKey, minioSecretKey)
            .WithSSL(minioUseSsl)
            .Build();

        services.AddSingleton<Minio.IMinioClient>(minioClient);

        // Public client for presigned URLs that browsers access directly
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
            publicMinioClient = minioClient;
        }
        services.AddKeyedSingleton<Minio.IMinioClient>("public", publicMinioClient);
    }
}
