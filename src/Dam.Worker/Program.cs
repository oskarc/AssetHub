using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Dam.Worker;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                var connectionString = hostContext.Configuration["ConnectionStrings:Postgres"];

                // Database
                services.AddDbContext<AssetHubDbContext>(options =>
                {
                    options.UseNpgsql(connectionString);
                    options.ConfigureWarnings(w =>
                        w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                });
            })
            .Build();

        // Initialize database on startup
        using (var scope = host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
            await dbContext.Database.MigrateAsync();
            Console.WriteLine("Database migration complete");
        }

        // Simply exit after migration
        Console.WriteLine("Worker service started successfully");
        await Task.Delay(Timeout.Infinite);
    }
}
