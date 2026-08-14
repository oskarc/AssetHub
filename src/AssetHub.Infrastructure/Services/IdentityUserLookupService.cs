using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// <see cref="IUserLookupService"/> backed by the local ASP.NET Core Identity
/// store. Selected when <c>Auth:Provider</c> is <c>Identity</c>; the Keycloak
/// implementation (<see cref="UserLookupService"/>) is used otherwise.
/// </summary>
/// <remarks>
/// The Keycloak implementation caches aggressively because every lookup is an
/// outbound admin-API call. Here the users live in the same database as the rows
/// being rendered, so each method is a single indexed query and caching would
/// add staleness for no gain.
/// </remarks>
public sealed class IdentityUserLookupService(
    DbContextProvider provider,
    ILogger<IdentityUserLookupService> logger) : IUserLookupService
{
    public async Task<Dictionary<string, string>> GetUserNamesAsync(
        IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Set<AppUser>()
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName ?? u.UserName })
            .Where(u => u.Name != null)
            .ToDictionaryAsync(u => u.Id, u => u.Name!, ct);
    }

    public async Task<string?> GetUserNameAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Set<AppUser>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName ?? u.UserName)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Dictionary<string, string>> GetUserEmailsAsync(
        IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Set<AppUser>()
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Email != null)
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email!, ct);
    }

    public async Task<string?> GetUserIdByUsernameAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;

        var normalized = username.ToUpperInvariant();
        await using var lease = await provider.AcquireAsync(ct);
        return await lease.Db.Set<AppUser>()
            .AsNoTracking()
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> UserExistsAsync(string username, CancellationToken ct = default)
        => await GetUserIdByUsernameAsync(username, ct) is not null;

    public async Task<List<(string Id, string Username, string? Email, string? FirstName, string? LastName, DateTime? CreatedAt)>>
        GetAllUsersAsync(CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var rows = await lease.Db.Set<AppUser>()
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new { u.Id, u.UserName, u.Email, u.DisplayName, u.CreatedAt })
            .ToListAsync(ct);

        // Identity has no first/last name split — DisplayName is the whole name.
        // Returning it as FirstName keeps the tuple shape the Keycloak
        // implementation established without inventing a surname.
        return rows
            .Select(u => (u.Id, u.UserName ?? string.Empty, u.Email, u.DisplayName, (string?)null, (DateTime?)u.CreatedAt))
            .ToList();
    }

    public async Task<List<(string Id, string Username, string? Email)>> SearchUsersAsync(
        string query, int maxResults = 50, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var q = lease.Db.Set<AppUser>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            q = q.Where(u => EF.Functions.ILike(u.UserName!, $"%{query}%")
                          || EF.Functions.ILike(u.Email!, $"%{query}%"));
        }

        var rows = await q
            .OrderBy(u => u.UserName)
            .Take(Math.Clamp(maxResults, 1, 500))
            .Select(u => new { u.Id, u.UserName, u.Email })
            .ToListAsync(ct);

        return rows.Select(u => (u.Id, u.UserName ?? string.Empty, u.Email)).ToList();
    }

    public async Task<HashSet<string>> GetExistingUserIdsAsync(
        IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new HashSet<string>();

        await using var lease = await provider.AcquireAsync(ct);
        var found = await lease.Db.Set<AppUser>()
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);

        logger.LogDebug("Resolved {Found}/{Asked} user ids against the local Identity store", found.Count, ids.Count);
        return found.ToHashSet();
    }
}
