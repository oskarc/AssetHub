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
            throw new InvalidOperationException(
                "EF_CONNECTION environment variable is not set. " +
                "Set it to a valid PostgreSQL connection string before running EF migrations.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AssetHubDbContext>();
        optionsBuilder.UseNpgsql(conn, o => o.EnableRetryOnFailure());

        return new AssetHubDbContext(optionsBuilder.Options);
    }
}
