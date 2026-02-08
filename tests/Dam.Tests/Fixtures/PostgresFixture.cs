using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Dam.Tests.Fixtures;

/// <summary>
/// Shared fixture that starts a PostgreSQL container once for all test classes.
/// Each test class creates its own unique database for isolation.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a new DbContext pointing at a unique per-call database.
    /// The database is created and the schema applied via EnsureCreated.
    /// </summary>
    public async Task<AssetHubDbContext> CreateDbContextAsync(string? dbName = null)
    {
        dbName ??= $"test_{Guid.NewGuid():N}";

        // Create the database first using the default connection
        await using var adminConn = new NpgsqlConnection(ConnectionString);
        await adminConn.OpenAsync();
        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();

        // Build connection string for the new database
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = dbName };
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();
        var options = new DbContextOptionsBuilder<AssetHubDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        var db = new AssetHubDbContext(options);
        await db.Database.EnsureCreatedAsync();

        // Create pg_trgm extension for ILike/trigram search
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

        return db;
    }

    /// <summary>
    /// Returns a connection string for a named database (must already be created).
    /// </summary>
    public string GetConnectionString(string dbName)
    {
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = dbName }.ConnectionString;
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<PostgresFixture> { }
