using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using AssetHub.Application;
using AssetHub.Application.Services;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Keycloak implementation of IUserLookupService.
/// Queries Keycloak's user_entity table directly for user information.
/// Uses the KeycloakDb connection string (separate database) with fallback to Postgres.
/// Caches username lookups (rarely change) and all-users list (short TTL) via HybridCache.
/// </summary>
public class UserLookupService(
    IConfiguration configuration,
    HybridCache cache,
    ILogger<UserLookupService> logger) : IUserLookupService
{
    private readonly string _connectionString = configuration.GetConnectionString("KeycloakDb") 
        ?? configuration.GetConnectionString("Postgres") 
        ?? throw new InvalidOperationException("KeycloakDb or Postgres connection string is required");

    public async Task<Dictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var idsToFetch = new List<string>();

        // Check cache for each individual user
        foreach (var id in userIds.Distinct())
        {
            var cacheKey = CacheKeys.UserName(id);
            var cached = await cache.GetOrCreateAsync(
                cacheKey,
                // Return null placeholder to detect miss — actual fetch is batched below
                cancel => default(ValueTask<string?>),
                new HybridCacheEntryOptions
                {
                    Expiration = CacheKeys.UserNameTtl,
                    LocalCacheExpiration = TimeSpan.FromMinutes(2)
                },
                [CacheKeys.Tags.UserNames],
                ct);

            if (cached is not null)
            {
                result[id] = cached;
            }
            else
            {
                idsToFetch.Add(id);
            }
        }

        if (idsToFetch.Count == 0)
        {
            logger.LogDebug("Cache hit: all {Count} usernames resolved from cache", result.Count);
            return result;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = "SELECT id, username FROM user_entity WHERE id = ANY(@ids)";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ids", idsToFetch.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var username = reader.GetString(1);
            result[id] = username;
            // Populate individual cache entry
            await cache.SetAsync(
                CacheKeys.UserName(id),
                username,
                new HybridCacheEntryOptions
                {
                    Expiration = CacheKeys.UserNameTtl,
                    LocalCacheExpiration = TimeSpan.FromMinutes(2)
                },
                [CacheKeys.Tags.UserNames],
                ct);
        }

        logger.LogDebug("Username lookup: {CacheHits} from cache, {DbFetches} from DB", result.Count - idsToFetch.Count, idsToFetch.Count);
        return result;
    }

    public async Task<string?> GetUserNameAsync(string userId, CancellationToken ct = default)
    {
        var map = await GetUserNamesAsync(new[] { userId }, ct);
        return map.TryGetValue(userId, out var username) ? username : null;
    }

    public async Task<Dictionary<string, string>> GetUserEmailsAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0) return result;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = "SELECT id, email FROM user_entity WHERE id = ANY(@ids) AND email IS NOT NULL";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ids", ids);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }
    
    public async Task<string?> GetUserIdByUsernameAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;
            
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = "SELECT id FROM user_entity WHERE LOWER(username) = LOWER(@username) LIMIT 1";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("username", username);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }
    
    public async Task<bool> UserExistsAsync(string username, CancellationToken ct = default)
    {
        var userId = await GetUserIdByUsernameAsync(username, ct);
        return userId != null;
    }
    
    public async Task<List<(string Id, string Username, string? Email, string? FirstName, string? LastName, DateTime? CreatedAt)>> GetAllUsersAsync(CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.AllUsers();

        return await cache.GetOrCreateAsync(
            cacheKey,
            async cancel =>
            {
                var users = new List<(string, string, string?, string?, string?, DateTime?)>();

                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancel);

                var sql = $"SELECT id, username, email, first_name, last_name, created_timestamp FROM user_entity ORDER BY username LIMIT {Constants.Limits.MaxUserQueryLimit}";
                await using var cmd = new NpgsqlCommand(sql, connection);

                await using var reader = await cmd.ExecuteReaderAsync(cancel);
                while (await reader.ReadAsync(cancel))
                {
                    var id = reader.GetString(0);
                    var username = reader.GetString(1);
                    var email = await reader.IsDBNullAsync(2, cancel) ? null : reader.GetString(2);
                    var firstName = await reader.IsDBNullAsync(3, cancel) ? null : reader.GetString(3);
                    var lastName = await reader.IsDBNullAsync(4, cancel) ? null : reader.GetString(4);
                    var createdTimestamp = await reader.IsDBNullAsync(5, cancel) ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)).UtcDateTime;
                    users.Add((id, username, email, firstName, lastName, createdTimestamp));

                    // Also populate the individual username cache
                    await cache.SetAsync(
                        CacheKeys.UserName(id),
                        username,
                        new HybridCacheEntryOptions
                        {
                            Expiration = CacheKeys.UserNameTtl,
                            LocalCacheExpiration = TimeSpan.FromMinutes(2)
                        },
                        [CacheKeys.Tags.UserNames],
                        cancel);
                }

                return users;
            },
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.AllUsersTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(15)
            },
            [CacheKeys.Tags.UserNames],
            ct);
    }

    public async Task<HashSet<string>> GetExistingUserIdsAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = "SELECT id FROM user_entity WHERE id = ANY(@ids)";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ids", ids);

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            existing.Add(reader.GetString(0));
        }

        return existing;
    }
}
