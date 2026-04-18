using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Orchestrates aggregated dashboard data from multiple typed services and repositories.
/// Raw database queries are delegated to <see cref="IDashboardQueryService"/>;
/// this class contains only orchestration and role-gating logic.
/// </summary>
public sealed class DashboardService(
    IDashboardQueryService queryService,
    ICollectionRepository collectionRepo,
    ICollectionAuthorizationService authService,
    IAssetRepository assetRepo,
    IUserLookupService userLookup,
    IKeycloakUserService keycloakUsers,
    CurrentUser currentUser) : IDashboardService
{
    private const int RecentAssetsLimit = 12;
    private const int RecentSharesLimit = 10;
    private const int RecentActivityLimit = 20;
    private const int QuickAccessCollectionsLimit = 12;

    public async Task<ServiceResult<DashboardDto>> GetDashboardAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        var isAdmin = currentUser.IsSystemAdmin;

        // Determine the user's highest role across all collections
        var highestRole = isAdmin
            ? RoleHierarchy.Roles.Admin
            : await queryService.GetHighestRoleAsync(userId, ct);

        var dashboard = new DashboardDto { UserRole = highestRole };

        var showManagerSections = isAdmin || RoleHierarchy.GetLevel(highestRole) >= RoleHierarchy.GetLevel(RoleHierarchy.Roles.Manager);

        // ── Start non-DB calls (Keycloak HTTP) in parallel with DB work ──
        var adminIdsTask = isAdmin
            ? keycloakUsers.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin, ct)
            : Task.FromResult<HashSet<string>>(null!);
        var allUsersTask = isAdmin
            ? userLookup.GetAllUsersAsync(ct)
            : Task.FromResult<List<(string Id, string Username, string? Email, string? FirstName, string? LastName, DateTime? CreatedAt)>>(null!);

        // ── DB queries must stay sequential (shared scoped DbContext) ────
        dashboard.RecentAssets = await GetRecentAssetsAsync(userId, isAdmin, ct);
        dashboard.Collections = await GetAccessibleCollectionsAsync(userId, ct);

        if (showManagerSections)
        {
            dashboard.RecentShares = await queryService.GetRecentSharesAsync(
                isAdmin ? null : userId, RecentSharesLimit, ct);
            dashboard.RecentActivity = await queryService.GetRecentActivityAsync(
                isAdmin ? null : userId, RecentActivityLimit, ct);
        }

        if (isAdmin)
        {
            dashboard.Stats = await queryService.GetGlobalStatsAsync(ct);

            var adminIds = await adminIdsTask;
            dashboard.Stats.AdminCount = adminIds.Count;

            var allUsers = await allUsersTask;
            dashboard.Stats.TotalUsers = allUsers.Count;
            var assignedCount = dashboard.Stats.AdminCount + dashboard.Stats.ViewerCount
                              + dashboard.Stats.ContributorCount + dashboard.Stats.ManagerCount;
            dashboard.Stats.UnassignedCount = Math.Max(0, dashboard.Stats.TotalUsers - assignedCount);
        }

        return dashboard;
    }

    private async Task<List<AssetResponseDto>> GetRecentAssetsAsync(string userId, bool isAdmin, CancellationToken ct)
    {
        List<AssetHub.Domain.Entities.Asset> assets;

        if (isAdmin)
        {
            var (items, _) = await assetRepo.SearchAllAsync(new AssetSearchFilter
            {
                SortBy = Constants.SortBy.CreatedDesc,
                Take = RecentAssetsLimit,
                IncludeAllStatuses = false
            }, ct);
            assets = items;
        }
        else
        {
            var collections = await collectionRepo.GetAccessibleCollectionsAsync(userId, ct);
            var collectionIds = collections.Select(c => c.Id).ToList();

            if (collectionIds.Count == 0)
                return [];

            var (items, _) = await assetRepo.SearchAllAsync(new AssetSearchFilter
            {
                SortBy = Constants.SortBy.CreatedDesc,
                Take = RecentAssetsLimit,
                AllowedCollectionIds = collectionIds,
                IncludeAllStatuses = false
            }, ct);
            assets = items;
        }

        var userIds = assets.Select(a => a.CreatedByUserId).Where(id => !string.IsNullOrEmpty(id)).Distinct();
        var userNames = await userLookup.GetUserNamesAsync(userIds, ct);

        return assets.Select(a => AssetMapper.ToDto(a,
            createdByUserName: userNames.GetValueOrDefault(a.CreatedByUserId))).ToList();
    }

    private async Task<List<CollectionResponseDto>> GetAccessibleCollectionsAsync(string userId, CancellationToken ct)
    {
        var collections = await collectionRepo.GetAccessibleCollectionsAsync(userId, ct);
        var collectionList = collections.ToList();

        if (collectionList.Count == 0)
            return [];

        var topCollections = collectionList.Take(QuickAccessCollectionsLimit).ToList();
        var collectionIds = topCollections.Select(c => c.Id).ToList();
        var assetCounts = await collectionRepo.GetAssetCountsAsync(collectionIds, ct);
        var dtos = await CollectionMapper.ToDtoListAsync(topCollections, userId, authService, assetCounts, ct);

        var latestUpdates = await queryService.GetLatestUpdatesByCollectionAsync(collectionIds, ct);

        dtos = dtos.Select(d => d with
        {
            UpdatedAt = latestUpdates.GetValueOrDefault(d.Id, d.CreatedAt)
        })
        .OrderByDescending(d => d.UpdatedAt)
        .ToList();

        return dtos;
    }
}

