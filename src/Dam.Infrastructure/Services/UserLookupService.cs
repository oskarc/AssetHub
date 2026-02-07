using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Dam.Application;
using Dam.Application.Services;

namespace Dam.Infrastructure.Services;

/// <summary>
/// Keycloak implementation of IUserLookupService.
/// Queries Keycloak's user_entity table directly for user information.
/// Caches username lookups (rarely change) and all-users list (short TTL).
/// </summary>
public class UserLookupService(
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<UserLookupService> logger) : IUserLookupService
{
    private readonly string _connectionString = configuration.GetConnectionString("Postgres") 
        ?? throw new InvalidOperationException("Postgres connection string is required");

    public async Task<Dictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var idsToFetch = new List<string>();

        foreach (var id in userIds.Distinct())
        {
            var cacheKey = CacheKeys.UserName(id);
            if (cache.TryGetValue(cacheKey, out string? cachedName) && cachedName is not null)
            {
                result[id] = cachedName;
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
            cache.Set(CacheKeys.UserName(id), username, CacheKeys.UserNameTtl);
        }

        logger.LogDebug("Username lookup: {CacheHits} from cache, {DbFetches} from DB", result.Count - idsToFetch.Count + (result.Count - idsToFetch.Count), idsToFetch.Count);
        return result;
    }

    public async Task<string?> GetUserNameAsync(string userId, CancellationToken ct = default)
    {
        var map = await GetUserNamesAsync(new[] { userId }, ct);
        return map.TryGetValue(userId, out var username) ? username : null;
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
    
    public async Task<List<(string Id, string Username, string? Email, DateTime? CreatedAt)>> GetAllUsersAsync(CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.AllUsers();

        if (cache.TryGetValue(cacheKey, out List<(string, string, string?, DateTime?)>? cached) && cached is not null)
        {
            logger.LogDebug("Cache hit: all users list ({Count} users)", cached.Count);
            return cached;
        }

        var result = new List<(string, string, string?, DateTime?)>();
        
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var sql = "SELECT id, username, email, created_timestamp FROM user_entity ORDER BY username";
        await using var cmd = new NpgsqlCommand(sql, connection);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var username = reader.GetString(1);
            var email = reader.IsDBNull(2) ? null : reader.GetString(2);
            var createdTimestamp = reader.IsDBNull(3) ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)).UtcDateTime;
            result.Add((id, username, email, createdTimestamp));
            
            // Also populate the individual username cache
            cache.Set(CacheKeys.UserName(id), username, CacheKeys.UserNameTtl);
        }

        cache.Set(cacheKey, result, CacheKeys.AllUsersTtl);
        return result;
    }
}
