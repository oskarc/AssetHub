using AssetHub.Application;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for DashboardService — role-scoped dashboard data.
/// Uses real DB via Testcontainers.
/// </summary>
[Collection("Database")]
public class DashboardServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;

    private const string AdminUser = "dash-admin-001";
    private const string ManagerUser = "dash-manager-001";
    private const string ViewerUser = "dash-viewer-001";

    public DashboardServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
    }

    private DashboardService CreateService(string userId, bool isAdmin = false,
        Dictionary<string, string>? userNames = null)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        var collectionRepo = new CollectionRepository(_db, NullLogger<CollectionRepository>.Instance);
        var assetRepo = new AssetRepository(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<AssetRepository>.Instance);
        var shareRepo = new ShareRepository(_db, NullLogger<ShareRepository>.Instance);
        var authService = new CollectionAuthorizationService(_db, NullLogger<CollectionAuthorizationService>.Instance);
        var userLookupMock = new Mock<IUserLookupService>();
        userLookupMock.Setup(m => m.GetUserNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userNames ?? new Dictionary<string, string>());

        return new DashboardService(
            _db, collectionRepo, authService, assetRepo, shareRepo,
            userLookupMock.Object, currentUser, NullLogger<DashboardService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── Admin dashboard ─────────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_Admin_GetsStatsAndAllData()
    {
        // Seed some data
        var col = TestData.CreateCollection(name: "AdminCol");
        var asset = TestData.CreateAsset(title: "AdminAsset", status: AssetStatus.Ready, sizeBytes: 1024);
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));
        _db.Shares.Add(TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: asset.Id, createdByUserId: AdminUser));
        _db.AuditEvents.Add(TestData.CreateAuditEvent("asset.created", Constants.ScopeTypes.Asset, asset.Id, AdminUser));
        await _db.SaveChangesAsync();

        var svc = CreateService(AdminUser, isAdmin: true);
        var result = await svc.GetDashboardAsync();

        Assert.True(result.IsSuccess);
        var dashboard = result.Value!;

        Assert.Equal(RoleHierarchy.Roles.Admin, dashboard.UserRole);
        Assert.NotNull(dashboard.Stats);
        Assert.True(dashboard.Stats!.TotalAssets >= 1);
        Assert.True(dashboard.Stats.TotalStorageBytes >= 1024);
        Assert.NotEmpty(dashboard.RecentAssets);
        Assert.NotEmpty(dashboard.RecentShares);
        Assert.NotEmpty(dashboard.RecentActivity);
    }

    [Fact]
    public async Task GetDashboard_Admin_StatsCountCorrectly()
    {
        var col = TestData.CreateCollection(name: "StatsCol");
        _db.Collections.Add(col);
        for (int i = 0; i < 3; i++)
        {
            var a = TestData.CreateAsset(title: $"Asset{i}", status: AssetStatus.Ready, sizeBytes: 100);
            _db.Assets.Add(a);
            _db.AssetCollections.Add(TestData.CreateAssetCollection(a.Id, col.Id));
        }
        await _db.SaveChangesAsync();

        var svc = CreateService(AdminUser, isAdmin: true);
        var result = await svc.GetDashboardAsync();
        var stats = result.Value!.Stats!;

        Assert.Equal(3, stats.TotalAssets);
        Assert.Equal(300, stats.TotalStorageBytes);
        Assert.Equal(1, stats.TotalCollections);
    }

    // ── Manager dashboard ───────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_Manager_GetsSharesAndActivity_NoStats()
    {
        var col = TestData.CreateCollection(name: "MgrCol");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        _db.AuditEvents.Add(TestData.CreateAuditEvent("collection.created", Constants.ScopeTypes.Collection, col.Id, ManagerUser));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        var result = await svc.GetDashboardAsync();

        Assert.True(result.IsSuccess);
        var dashboard = result.Value!;

        Assert.Equal(RoleHierarchy.Roles.Manager, dashboard.UserRole);
        Assert.Null(dashboard.Stats); // managers don't get global stats
        Assert.NotEmpty(dashboard.RecentActivity);
    }

    // ── Viewer dashboard ────────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_Viewer_NoSharesOrActivity()
    {
        var col = TestData.CreateCollection(name: "ViewerCol");
        var asset = TestData.CreateAsset(title: "ViewerAsset", status: AssetStatus.Ready);
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ViewerUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var svc = CreateService(ViewerUser);
        var result = await svc.GetDashboardAsync();

        Assert.True(result.IsSuccess);
        var dashboard = result.Value!;

        Assert.Equal(RoleHierarchy.Roles.Viewer, dashboard.UserRole);
        Assert.Null(dashboard.Stats);
        Assert.Empty(dashboard.RecentShares);
        Assert.Empty(dashboard.RecentActivity);
        Assert.NotEmpty(dashboard.RecentAssets);
    }

    // ── No access at all ────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_NoAccess_ReturnsEmptyDashboard()
    {
        var svc = CreateService("unknown-user-001");
        var result = await svc.GetDashboardAsync();

        Assert.True(result.IsSuccess);
        var dashboard = result.Value!;

        Assert.Equal(RoleHierarchy.Roles.Viewer, dashboard.UserRole); // defaults to viewer
        Assert.Empty(dashboard.RecentAssets);
        Assert.Empty(dashboard.Collections);
        Assert.Null(dashboard.Stats);
    }

    // ── Collection scoping ──────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_NonAdmin_OnlySeesAccessibleCollectionAssets()
    {
        var colAccessible = TestData.CreateCollection(name: "Accessible");
        var colHidden = TestData.CreateCollection(name: "Hidden");
        var assetAccessible = TestData.CreateAsset(title: "Visible", status: AssetStatus.Ready);
        var assetHidden = TestData.CreateAsset(title: "NotVisible", status: AssetStatus.Ready);

        _db.Collections.AddRange(colAccessible, colHidden);
        _db.Assets.AddRange(assetAccessible, assetHidden);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(assetAccessible.Id, colAccessible.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(assetHidden.Id, colHidden.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(colAccessible.Id, ViewerUser, AclRole.Viewer));
        // No ACL for colHidden for ViewerUser
        await _db.SaveChangesAsync();

        var svc = CreateService(ViewerUser);
        var result = await svc.GetDashboardAsync();

        Assert.True(result.IsSuccess);
        var assetTitles = result.Value!.RecentAssets.Select(a => a.Title).ToList();
        Assert.Contains("Visible", assetTitles);
        Assert.DoesNotContain("NotVisible", assetTitles);
    }

    // ── Username resolution ─────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_ResolvesCreatorAndActorUserNames()
    {
        var col = TestData.CreateCollection(name: "UserNameCol");
        var asset = TestData.CreateAsset(title: "NamedAsset", status: AssetStatus.Ready,
            createdByUserId: AdminUser);
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));
        _db.AuditEvents.Add(TestData.CreateAuditEvent(
            "asset.created", Constants.ScopeTypes.Asset, asset.Id, AdminUser));
        await _db.SaveChangesAsync();

        var nameMap = new Dictionary<string, string>
        {
            [AdminUser] = "Admin Display Name"
        };
        var svc = CreateService(AdminUser, isAdmin: true, userNames: nameMap);
        var result = await svc.GetDashboardAsync();

        Assert.True(result.IsSuccess);
        var dashboard = result.Value!;

        // Asset creator username resolved
        var recentAsset = dashboard.RecentAssets.First(a => a.Title == "NamedAsset");
        Assert.Equal("Admin Display Name", recentAsset.CreatedByUserName);

        // Activity actor username resolved
        var activity = dashboard.RecentActivity.First();
        Assert.Equal("Admin Display Name", activity.ActorUserName);
    }

    [Fact]
    public async Task GetDashboard_Share_StatusIsLowercase()
    {
        var col = TestData.CreateCollection(name: "ShareStatusCol");
        var assetA = TestData.CreateAsset(title: "ActiveAsset", status: AssetStatus.Ready);
        var assetB = TestData.CreateAsset(title: "ExpiredAsset", status: AssetStatus.Ready);
        var assetC = TestData.CreateAsset(title: "RevokedAsset", status: AssetStatus.Ready);
        _db.Collections.Add(col);
        _db.Assets.AddRange(assetA, assetB, assetC);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(assetA.Id, col.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(assetB.Id, col.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(assetC.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));

        _db.Shares.Add(TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: assetA.Id,
            createdByUserId: AdminUser, expiresAt: DateTime.UtcNow.AddDays(7)));
        _db.Shares.Add(TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: assetB.Id,
            createdByUserId: AdminUser, expiresAt: DateTime.UtcNow.AddDays(-1)));
        _db.Shares.Add(TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: assetC.Id,
            createdByUserId: AdminUser, revoked: true));
        await _db.SaveChangesAsync();

        var svc = CreateService(AdminUser, isAdmin: true);
        var result = await svc.GetDashboardAsync();

        Assert.True(result.IsSuccess);
        var shares = result.Value!.RecentShares;
        Assert.Contains(shares, s => s.Status == "active");
        Assert.Contains(shares, s => s.Status == "expired");
        Assert.Contains(shares, s => s.Status == "revoked");
        // Statuses should always be lowercase for UI compatibility
        Assert.All(shares, s => Assert.Equal(s.Status, s.Status.ToLowerInvariant()));
    }
}

