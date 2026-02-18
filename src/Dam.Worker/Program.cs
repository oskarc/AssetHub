using Dam.Infrastructure.Data;
using Dam.Infrastructure.DependencyInjection;
using Dam.Worker.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dam.Worker;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                // Shared infrastructure: DB, Hangfire storage, MinIO, Repos, core services
                services.AddSharedInfrastructure(hostContext.Configuration);

                // Hangfire server (Worker processes jobs with custom queue/worker config)
                services.AddHangfireServer(options =>
                {
                    options.Queues = new[] { "default", "media-processing" };
                    options.WorkerCount = Environment.ProcessorCount * 2;
                });

                // Worker-specific services
                services.AddScoped<StaleUploadCleanupJob>();
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

        Console.WriteLine("Worker service started — Hangfire server processing jobs");
        await host.RunAsync();
    }
}
