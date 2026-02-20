using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AssetHub.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AssetHubDbContext>
{
    public AssetHubDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("EF_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            // Fallback to a sensible local default (development)
            conn = "Server=localhost;Port=5432;Database=assethub;User Id=postgres;Password=postgres_dev_password;";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AssetHubDbContext>();
        optionsBuilder.UseNpgsql(conn, o => o.EnableRetryOnFailure());

        return new AssetHubDbContext(optionsBuilder.Options);
    }
}
