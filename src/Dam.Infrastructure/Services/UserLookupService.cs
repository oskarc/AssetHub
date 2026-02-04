using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dam.Application.Services;

namespace Dam.Infrastructure.Services;

/// <summary>
/// Keycloak implementation of IUserLookupService.
/// Queries Keycloak's user_entity table directly for user information.
/// </summary>
public class UserLookupService(
    IConfiguration configuration,
    ILogger<UserLookupService> logger) : IUserLookupService
{
    private readonly string _connectionString = configuration.GetConnectionString("Postgres") 
        ?? throw new InvalidOperationException("Postgres connection string is required");

    public async Task<Dictionary<string, string>> GetUserNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var idList = userIds.Distinct().ToList();
        
        if (idList.Count == 0)
            return result;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Query Keycloak's user_entity table directly
        var sql = "SELECT id, username FROM user_entity WHERE id = ANY(@ids)";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("ids", idList.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var username = reader.GetString(1);
            result[id] = username;
        }

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
        }

        return result;
    }
}
