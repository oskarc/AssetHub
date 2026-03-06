using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.DependencyInjection;
using AssetHub.Infrastructure.Services;
using AssetHub.Worker.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssetHub.Worker;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                // Memory cache — required by repository layer (registered via AddSharedInfrastructure)
                services.AddMemoryCache();

                // Shared infrastructure: DB, Hangfire storage, MinIO, Repos, core services
                services.AddSharedInfrastructure(hostContext.Configuration);

                // Worker-specific services needed for job resolution
                services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>(); // Returns null HttpContext for Worker
                services.AddScoped<IAuditService, AuditService>();

                // OpenTelemetry for distributed tracing
                services.AddWorkerOpenTelemetry(hostContext.Configuration);

                // Hangfire server (Worker processes jobs with custom queue/worker config)
                services.AddHangfireServer(options =>
                {
                    options.Queues = new[] { "default", "media-processing" };
                    options.WorkerCount = Math.Max(AssetHub.Application.Constants.Limits.WorkerMinHangfireWorkers, Math.Min(Environment.ProcessorCount, AssetHub.Application.Constants.Limits.WorkerMaxHangfireWorkers));
                });

                // Worker-specific services
                services.AddScoped<StaleUploadCleanupJob>();
                services.AddScoped<CleanupOrphanedSharesJob>();
                services.AddScoped<AuditRetentionJob>();
            })
            .Build();

        // ── Initialize database ────────────────────────────────────────────
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
            await dbContext.Database.MigrateAsync();
            Console.WriteLine("Database migration complete");
        }

        // ── Register recurring Hangfire jobs ───────────────────────────────
        var recurringJobs = host.Services.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate<StaleUploadCleanupJob>(
            "stale-upload-cleanup",
            job => job.ExecuteAsync(),
            Cron.Daily(3, 0), // Run daily at 3:00 AM UTC
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        recurringJobs.AddOrUpdate<CleanupOrphanedSharesJob>(
            "orphaned-shares-cleanup",
            job => job.ExecuteAsync(),
            Cron.Weekly(DayOfWeek.Sunday, 4, 0), // Run weekly on Sunday at 4:00 AM UTC
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        recurringJobs.AddOrUpdate<AuditRetentionJob>(
            "audit-retention",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Weekly(DayOfWeek.Sunday, 5, 0), // Run weekly on Sunday at 5:00 AM UTC
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        Console.WriteLine("Worker service started — Hangfire server processing jobs");
        await host.RunAsync();
    }
}
