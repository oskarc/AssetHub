using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

/// <summary>
/// Provides aggregated dashboard data scoped by the requesting user's role.
/// </summary>
public class DashboardService : IDashboardService
{
    private const int RecentAssetsLimit = 12;
    private const int RecentSharesLimit = 10;
    private const int RecentActivityLimit = 20;

    private readonly AssetHubDbContext _db;
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICollectionAuthorizationService _authService;
    private readonly IAssetRepository _assetRepo;
    private readonly IShareRepository _shareRepo;
    private readonly CurrentUser _currentUser;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        AssetHubDbContext db,
        ICollectionRepository collectionRepo,
        ICollectionAuthorizationService authService,
        IAssetRepository assetRepo,
        IShareRepository shareRepo,
        CurrentUser currentUser,
        ILogger<DashboardService> logger)
    {
        _db = db;
        _collectionRepo = collectionRepo;
        _authService = authService;
        _assetRepo = assetRepo;
        _shareRepo = shareRepo;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ServiceResult<DashboardDto>> GetDashboardAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        var isAdmin = _currentUser.IsSystemAdmin;

        // Determine the user's highest role across all collections
        var highestRole = isAdmin
            ? RoleHierarchy.Roles.Admin
            : await ResolveHighestRoleAsync(userId, ct);

        var dashboard = new DashboardDto { UserRole = highestRole };

        // ── Recent assets ───────────────────────────────────────────────
        dashboard.RecentAssets = await GetRecentAssetsAsync(userId, isAdmin, ct);

        // ── Collections (for contributor/viewer quick access) ────────────
        dashboard.Collections = await GetAccessibleCollectionsAsync(userId, ct);

        // ── Shares (manager+ see their own, admin sees all) ─────────────
        if (isAdmin || RoleHierarchy.GetLevel(highestRole) >= RoleHierarchy.GetLevel(RoleHierarchy.Roles.Manager))
        {
            dashboard.RecentShares = await GetRecentSharesAsync(userId, isAdmin, ct);
        }

        // ── Activity feed (manager+ see their own, admin sees all) ──────
        if (isAdmin || RoleHierarchy.GetLevel(highestRole) >= RoleHierarchy.GetLevel(RoleHierarchy.Roles.Manager))
        {
            dashboard.RecentActivity = await GetRecentActivityAsync(userId, isAdmin, ct);
        }

        // ── Global stats (admin only) ───────────────────────────────────
        if (isAdmin)
        {
            dashboard.Stats = await GetGlobalStatsAsync(ct);
        }

        return dashboard;
    }

    private async Task<string> ResolveHighestRoleAsync(string userId, CancellationToken ct)
    {
        // Get all ACL entries for this user and find the highest role
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

    private async Task<List<AssetResponseDto>> GetRecentAssetsAsync(string userId, bool isAdmin, CancellationToken ct)
    {
        List<Asset> assets;

        if (isAdmin)
        {
            // Admin sees all recent assets
            var (items, _) = await _assetRepo.SearchAllAsync(
                sortBy: "created_desc",
                take: RecentAssetsLimit,
                includeAllStatuses: false,
                cancellationToken: ct);
            assets = items;
        }
        else
        {
            // Non-admin: get assets from accessible collections only
            var collections = await _collectionRepo.GetAccessibleCollectionsAsync(userId, ct);
            var collectionIds = collections.Select(c => c.Id).ToList();

            if (collectionIds.Count == 0)
                return [];

            var (items, _) = await _assetRepo.SearchAllAsync(
                sortBy: "created_desc",
                take: RecentAssetsLimit,
                allowedCollectionIds: collectionIds,
                includeAllStatuses: false,
                cancellationToken: ct);
            assets = items;
        }

        return assets.Select(a => AssetMapper.ToDto(a)).ToList();
    }

    private async Task<List<CollectionResponseDto>> GetAccessibleCollectionsAsync(string userId, CancellationToken ct)
    {
        var collections = await _collectionRepo.GetAccessibleCollectionsAsync(userId, ct);
        var collectionList = collections.ToList();

        if (collectionList.Count == 0)
            return [];

        // Show root-level entry points (same logic as CollectionService.GetRootCollectionsAsync)
        var accessibleIds = collectionList.Select(c => c.Id).ToHashSet();
        var entryPoints = collectionList
            .Where(c => c.ParentId == null || !accessibleIds.Contains(c.ParentId.Value))
            .Take(20)
            .ToList();

        return await CollectionMapper.ToDtoListAsync(entryPoints, userId, _authService, ct);
    }

    private async Task<List<DashboardShareDto>> GetRecentSharesAsync(string userId, bool isAdmin, CancellationToken ct)
    {
        var query = _db.Shares
            .Include(s => s.Asset)
            .Include(s => s.Collection)
            .AsNoTracking();

        if (!isAdmin)
        {
            query = query.Where(s => s.CreatedByUserId == userId);
        }

        var shares = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(RecentSharesLimit)
            .ToListAsync(ct);

        return shares.Select(s => new DashboardShareDto
        {
            Id = s.Id,
            ScopeType = s.ScopeType.ToDbString(),
            ScopeId = s.ScopeId,
            ScopeName = s.ScopeType == ShareScopeType.Asset
                ? s.Asset?.Title
                : s.Collection?.Name,
            CreatedAt = s.CreatedAt,
            ExpiresAt = s.ExpiresAt,
            AccessCount = s.AccessCount,
            HasPassword = s.PasswordHash != null,
            Status = s.RevokedAt != null
                ? "revoked"
                : s.ExpiresAt < DateTime.UtcNow
                    ? "expired"
                    : "active"
        }).ToList();
    }

    private async Task<List<AuditEventDto>> GetRecentActivityAsync(string userId, bool isAdmin, CancellationToken ct)
    {
        var query = _db.AuditEvents.AsNoTracking();

        if (!isAdmin)
        {
            query = query.Where(e => e.ActorUserId == userId);
        }

        var events = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(RecentActivityLimit)
            .ToListAsync(ct);

        return events.Select(e => new AuditEventDto
        {
            EventType = e.EventType,
            TargetType = e.TargetType,
            TargetId = e.TargetId,
            ActorUserId = e.ActorUserId,
            CreatedAt = e.CreatedAt,
            Details = e.DetailsJson
        }).ToList();
    }

    private async Task<DashboardStatsDto> GetGlobalStatsAsync(CancellationToken ct)
    {
        // Run stat queries in parallel for efficiency
        var totalAssetsTask = _db.Assets.CountAsync(ct);
        var totalStorageTask = _db.Assets
            .Where(a => a.Status == AssetStatus.Ready)
            .SumAsync(a => a.SizeBytes, ct);
        var totalCollectionsTask = _db.Collections.CountAsync(ct);
        var totalSharesTask = _db.Shares.CountAsync(ct);
        var activeSharesTask = _db.Shares
            .CountAsync(s => s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow, ct);
        var totalUsersTask = _db.CollectionAcls
            .Where(a => a.PrincipalType == PrincipalType.User)
            .Select(a => a.PrincipalId)
            .Distinct()
            .CountAsync(ct);

        await Task.WhenAll(totalAssetsTask, totalStorageTask, totalCollectionsTask,
            totalSharesTask, activeSharesTask, totalUsersTask);

        return new DashboardStatsDto
        {
            TotalAssets = totalAssetsTask.Result,
            TotalStorageBytes = totalStorageTask.Result,
            TotalCollections = totalCollectionsTask.Result,
            TotalShares = totalSharesTask.Result,
            ActiveShares = activeSharesTask.Result,
            TotalUsers = totalUsersTask.Result
        };
    }
}
