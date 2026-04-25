using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Messages;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.DependencyInjection;
using AssetHub.Infrastructure.Services;
using AssetHub.Worker.BackgroundServices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;

namespace AssetHub.Worker;

static class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .UseWolverine(opts =>
            {
                opts.ApplicationAssembly = typeof(Program).Assembly;

                opts.UseRabbitMq(rabbit =>
                {
                    // Settings are bound below via IOptions; read raw config here for bootstrap
                    var config = opts.Services.BuildServiceProvider()
                        .GetRequiredService<IConfiguration>();
                    var section = config.GetSection(RabbitMQSettings.SectionName);
                    rabbit.HostName = section["Host"] ?? "localhost";
                    rabbit.VirtualHost = section["VirtualHost"] ?? "/";
                    rabbit.UserName = section["Username"] ?? "guest";
                    rabbit.Password = section["Password"] ?? "guest";
                }).AutoProvision();

                // Listen for commands from API
                opts.ListenToRabbitQueue("process-image");
                opts.ListenToRabbitQueue("process-video");
                opts.ListenToRabbitQueue("build-zip");
                opts.ListenToRabbitQueue("apply-export-presets");
                opts.ListenToRabbitQueue("start-migration");
                opts.ListenToRabbitQueue("process-migration-item");
                opts.ListenToRabbitQueue("s3-migration-scan");
                opts.ListenToRabbitQueue("send-notification-email");
                opts.ListenToRabbitQueue("dispatch-webhook");

                // Route events back to API
                opts.PublishMessage<AssetProcessingCompletedEvent>()
                    .ToRabbitQueue("asset-processing-completed");
                opts.PublishMessage<AssetProcessingFailedEvent>()
                    .ToRabbitQueue("asset-processing-failed");

                opts.Policies.AutoApplyTransactions();

                opts.OnException<Exception>().RetryWithCooldown(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30));
            })
            .ConfigureServices((hostContext, services) =>
            {
                // Shared infrastructure: DB, MinIO, Repos, Caching, core services
                services.AddSharedInfrastructure(hostContext.Configuration);

                // Worker-specific services needed for job resolution
                services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>(); // Returns null HttpContext for Worker
                services.AddScoped<IAuditService, AuditService>();

                // CurrentUser is HTTP-scoped in the API. In the Worker there is no
                // HttpContext, so we register an always-anonymous instance per
                // scope. Services that take CurrentUser (NotificationService,
                // NotificationPreferencesService, etc.) call it with an explicit
                // userId for write paths; for read-current-user paths the worker
                // does not call them.
                services.AddScoped<CurrentUser>(_ => CurrentUser.Anonymous);

                // Email + Keycloak directory lookups for notification fan-out.
                // These were API-only before phase 3; moving the interfaces to
                // the Worker lets SendNotificationEmailHandler and the digest
                // worker reach SMTP + Keycloak without going through the API.
                services.AddScoped<IUserLookupService, UserLookupService>();
                services.AddScoped<IEmailService, SmtpEmailService>();

                // Outbound HTTP client used by DispatchWebhookHandler. Short
                // timeout so a slow / hanging receiver can't hold a worker
                // thread for minutes; Wolverine retries handle the rest.
                services.AddHttpClient("webhook-dispatch", client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                });

                // Bind settings the worker-side services need
                services.AddOptions<AppSettings>()
                    .Bind(hostContext.Configuration.GetSection(AppSettings.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                services.Configure<EmailSettings>(hostContext.Configuration.GetSection(EmailSettings.SectionName));

                // OpenTelemetry for distributed tracing
                services.AddWorkerOpenTelemetry(hostContext.Configuration);

                // ── RabbitMQ settings validation ─────────────────────────────
                services.AddOptions<RabbitMQSettings>()
                    .BindConfiguration(RabbitMQSettings.SectionName)
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                // ── Background services (recurring tasks) ───────────────────
                services.AddHostedService<StaleUploadCleanupService>();
                services.AddHostedService<OrphanedSharesCleanupService>();
                services.AddHostedService<AuditRetentionService>();
                services.AddHostedService<TrashPurgeBackgroundService>();
                services.AddHostedService<SavedSearchDigestBackgroundService>();
                services.AddHostedService<GuestInvitationExpirySweepService>();
            })
            .Build();

        // ── Initialize database (only if AutoMigrate is enabled) ─────────
        var autoMigrate = host.Services.GetRequiredService<IConfiguration>()
            .GetValue("Database:AutoMigrate", true);
        if (autoMigrate)
        {
            using var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
            await dbContext.Database.MigrateAsync();
            Console.WriteLine("Database migration complete");
        }

        Console.WriteLine("Worker service started — Wolverine handlers processing messages");
        await host.RunAsync();
    }
}
