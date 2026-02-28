using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IDashboardQueryService"/> by executing raw EF Core queries
/// against the database. Isolates all direct <see cref="AssetHubDbContext"/> access from
/// the orchestrating <see cref="DashboardService"/>.
/// </summary>
public class DashboardQueryService : IDashboardQueryService
{
    private readonly AssetHubDbContext _db;
    private readonly IUserLookupService _userLookup;

    public DashboardQueryService(AssetHubDbContext db, IUserLookupService userLookup)
    {
        _db = db;
        _userLookup = userLookup;
    }

    public async Task<string> GetHighestRoleAsync(string userId, CancellationToken ct)
    {
        var acls = await _db.CollectionAcls
            .Where(a => a.PrincipalId == userId)
            .Select(a => a.Role)
            .Distinct()
            .ToListAsync(ct);

        if (acls.Count == 0)
            return RoleHierarchy.Roles.Viewer;

        return acls
            .OrderByDescending(r => (int)r)
            .First()
            .ToDbString();
    }

    public async Task<Dictionary<Guid, DateTime>> GetLatestUpdatesByCollectionAsync(
        IEnumerable<Guid> collectionIds, CancellationToken ct)
    {
        return await _db.AssetCollections
            .AsNoTracking()
            .Where(ac => collectionIds.Contains(ac.CollectionId))
            .GroupBy(ac => ac.CollectionId)
            .Select(g => new { CollectionId = g.Key, LatestUpdate = g.Max(ac => ac.Asset.UpdatedAt) })
            .ToDictionaryAsync(x => x.CollectionId, x => x.LatestUpdate, ct);
    }

    public async Task<List<DashboardShareDto>> GetRecentSharesAsync(
        string? userId, int take, CancellationToken ct)
    {
        var query = _db.Shares.AsNoTracking();
        if (userId != null)
            query = query.Where(s => s.CreatedByUserId == userId);

        var shares = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        var assetScopeIds = shares
            .Where(s => s.ScopeType == ShareScopeType.Asset)
            .Select(s => s.ScopeId).Distinct().ToList();
        var collectionScopeIds = shares
            .Where(s => s.ScopeType == ShareScopeType.Collection)
            .Select(s => s.ScopeId).Distinct().ToList();

        var assetNames = assetScopeIds.Count > 0
            ? await _db.Assets.Where(a => assetScopeIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Title, ct)
            : new Dictionary<Guid, string>();
        var collectionNames = collectionScopeIds.Count > 0
            ? await _db.Collections.Where(c => collectionScopeIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            : new Dictionary<Guid, string>();

        return shares.Select(s => new DashboardShareDto
        {
            Id = s.Id,
            ScopeType = s.ScopeType.ToDbString(),
            ScopeId = s.ScopeId,
            ScopeName = s.ScopeType == ShareScopeType.Asset
                ? (assetNames.TryGetValue(s.ScopeId, out var aName) ? aName : null)
                : (collectionNames.TryGetValue(s.ScopeId, out var cName) ? cName : null),
            CreatedAt = s.CreatedAt,
            ExpiresAt = s.ExpiresAt,
            AccessCount = s.AccessCount,
            HasPassword = s.PasswordHash != null,
            Status = ShareHelpers.GetShareStatus(s.RevokedAt, s.ExpiresAt).ToLowerInvariant()
        }).ToList();
    }

    public async Task<List<AuditEventDto>> GetRecentActivityAsync(
        string? userId, int take, CancellationToken ct)
    {
        var query = _db.AuditEvents.AsNoTracking();
        if (userId != null)
            query = query.Where(e => e.ActorUserId == userId);

        var events = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        var actorIds = events.Select(e => e.ActorUserId)
            .Where(id => !string.IsNullOrEmpty(id)).Distinct();
        var actorNames = await _userLookup.GetUserNamesAsync(actorIds!, ct);

        var assetIds = events
            .Where(e => e.TargetId.HasValue && e.TargetType == Constants.ScopeTypes.Asset)
            .Select(e => e.TargetId!.Value).Distinct().ToList();
        var collectionIds = events
            .Where(e => e.TargetId.HasValue && e.TargetType == Constants.ScopeTypes.Collection)
            .Select(e => e.TargetId!.Value).Distinct().ToList();

        var assetNames = await _db.Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Title })
            .ToDictionaryAsync(a => a.Id, a => a.Title, ct);

        var collectionNames = await _db.Collections.AsNoTracking()
            .Where(c => collectionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return events.Select(e => new AuditEventDto
        {
            EventType = e.EventType,
            TargetType = e.TargetType,
            TargetId = e.TargetId,
            TargetName = ResolveTargetName(e.TargetType, e.TargetId, assetNames, collectionNames),
            ActorUserId = e.ActorUserId,
            ActorUserName = e.ActorUserId != null ? actorNames.GetValueOrDefault(e.ActorUserId) : null,
            CreatedAt = e.CreatedAt,
            Details = e.DetailsJson
        }).ToList();
    }

    public async Task<DashboardStatsDto> GetGlobalStatsAsync(CancellationToken ct)
    {
        var totalAssets = await _db.Assets.CountAsync(ct);
        var totalStorage = await _db.Assets
            .Where(a => a.Status == AssetStatus.Ready)
            .SumAsync(a => a.SizeBytes, ct);
        var totalCollections = await _db.Collections.CountAsync(ct);
        var totalShares = await _db.Shares.CountAsync(ct);
        var activeShares = await _db.Shares
            .CountAsync(s => s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow, ct);
        var totalUsers = await _db.CollectionAcls
            .Where(a => a.PrincipalType == PrincipalType.User)
            .Select(a => a.PrincipalId)
            .Distinct()
            .CountAsync(ct);

        return new DashboardStatsDto
        {
            TotalAssets = totalAssets,
            TotalStorageBytes = totalStorage,
            TotalCollections = totalCollections,
            TotalShares = totalShares,
            ActiveShares = activeShares,
            TotalUsers = totalUsers
        };
    }

    private static string? ResolveTargetName(
        string targetType, Guid? targetId,
        Dictionary<Guid, string> assetNames, Dictionary<Guid, string> collectionNames)
    {
        if (!targetId.HasValue) return null;
        return targetType switch
        {
            Constants.ScopeTypes.Asset => assetNames.GetValueOrDefault(targetId.Value),
            Constants.ScopeTypes.Collection => collectionNames.GetValueOrDefault(targetId.Value),
            _ => null
        };
    }
}
