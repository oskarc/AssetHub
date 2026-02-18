using AssetHub.HealthChecks;
using Dam.Application;
using Dam.Application.Configuration;
using Dam.Application.Services;
using Dam.Infrastructure.Data;
using Dam.Infrastructure.DependencyInjection;
using Dam.Infrastructure.Services;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

        // ── Shared infrastructure: DB, Hangfire storage, MinIO, Repos, core services
        services.AddSharedInfrastructure(configuration);

        // ── Hangfire server (API host processes jobs with default options) ───
        services.AddHangfireServer();

        // ── MudBlazor ───────────────────────────────────────────────────────
        services.AddMudServices();

        // ── Caching ─────────────────────────────────────────────────────────
        services.AddMemoryCache();

        // ── Options (API-specific — shared options are in AddSharedInfrastructure) ─
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.Configure<KeycloakSettings>(configuration.GetSection(KeycloakSettings.SectionName));
        services.Configure<AppSettings>(configuration.GetSection(AppSettings.SectionName));

        // ── Application & Domain Services (API-only) ────────────────────────
        services.AddScoped<ICollectionAuthorizationService, CollectionAuthorizationService>();
        services.AddScoped<IUserLookupService, UserLookupService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserSyncService, UserSyncService>();
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

        var connectionString = configuration.GetConnectionString("Postgres");

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
}
