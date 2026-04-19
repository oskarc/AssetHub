using System.Collections.Concurrent;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AssetHub.Tests.Fixtures;

/// <summary>
/// Shared fixture that starts a PostgreSQL container once for all test classes.
/// Each test class creates its own unique database for isolation.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    // Reuse one NpgsqlDataSource per database name to avoid connection-pool
    // exhaustion caused by creating (but never disposing) a fresh data source
    // for every CreateDbContextAsync / CreateDbContextForExistingDb call.
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> _dataSources = new();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        var exceptions = new List<Exception>();
        foreach (var ds in _dataSources.Values)
        {
            try { await ds.DisposeAsync(); }
            catch (Exception ex) { exceptions.Add(ex); }
        }
        _dataSources.Clear();
        await _container.DisposeAsync();
        if (exceptions.Count > 0)
            throw new AggregateException("One or more data sources failed to dispose.", exceptions);
    }

    /// <summary>
    /// Returns (creating if necessary) a shared NpgsqlDataSource for the given database.
    /// </summary>
    private NpgsqlDataSource GetOrCreateDataSource(string dbName)
    {
        return _dataSources.GetOrAdd(dbName, name =>
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name };
            var dsBuilder = new NpgsqlDataSourceBuilder(builder.ConnectionString);
            dsBuilder.EnableDynamicJson();
            return dsBuilder.Build();
        });
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

        var dataSource = GetOrCreateDataSource(dbName);
        var options = new DbContextOptionsBuilder<AssetHubDbContext>()
            .UseNpgsql(dataSource)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
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

    /// <summary>
    /// Creates a new DbContext with the full EF migration set applied (including raw-SQL functions
    /// and triggers). Use this when the test exercises Postgres-specific behaviour that migrations
    /// install — the search_vector tsvector + its triggers, pg_trgm GIN indexes, etc.
    /// </summary>
    public async Task<AssetHubDbContext> CreateMigratedDbContextAsync(string? dbName = null)
    {
        dbName ??= $"test_{Guid.NewGuid():N}";

        await using var adminConn = new NpgsqlConnection(ConnectionString);
        await adminConn.OpenAsync();
        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();

        var dataSource = GetOrCreateDataSource(dbName);
        var options = new DbContextOptionsBuilder<AssetHubDbContext>()
            .UseNpgsql(dataSource)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var db = new AssetHubDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    /// <summary>
    /// Creates a new DbContext for an already-existing database.
    /// Useful when you need a fresh change tracker to avoid navigation property bleed.
    /// </summary>
    public AssetHubDbContext CreateDbContextForExistingDb(string dbName)
    {
        var dataSource = GetOrCreateDataSource(dbName);
        var options = new DbContextOptionsBuilder<AssetHubDbContext>()
            .UseNpgsql(dataSource)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new AssetHubDbContext(options);
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<PostgresFixture> { }
