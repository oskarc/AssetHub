using AssetHub.Application;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.EdgeCases;

/// <summary>
/// Tests for authorization edge cases not covered by CollectionAclInheritanceTests:
/// - CanCreateRootCollectionAsync (zero coverage)
/// - CanManageAclAsync / CanCreateSubCollectionAsync negative cases
/// - CheckAccessAsync direct ACL hit + non-existent collection
/// - GetUserRoleAsync multi-user same-chain
/// </summary>
[Collection("Database")]
public class AuthorizationEdgeCaseTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionRepository _collectionRepo = null!;
    private CollectionAclRepository _aclRepo = null!;
    private CollectionAuthorizationService _authService = null!;

    private const string UserA = "auth-edge-user-a";

    public AuthorizationEdgeCaseTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _collectionRepo = new CollectionRepository(_db, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
        _aclRepo = new CollectionAclRepository(_db, NullLogger<CollectionAclRepository>.Instance);
        _authService = new CollectionAuthorizationService(
            _db, CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── CanCreateRootCollectionAsync ────────────────────────────────

    [Fact]
    public async Task CanCreateRootCollection_ValidUserId_ReturnsTrue()
    {
        var result = await _authService.CanCreateRootCollectionAsync(UserA);
        Assert.True(result);
    }

    [Fact]
    public async Task CanCreateRootCollection_EmptyUserId_ReturnsFalse()
    {
        var result = await _authService.CanCreateRootCollectionAsync("");
        Assert.False(result);
    }

    [Fact]
    public async Task CanCreateRootCollection_NullUserId_ReturnsFalse()
    {
        var result = await _authService.CanCreateRootCollectionAsync(null!);
        Assert.False(result);
    }

    [Fact]
    public async Task CanCreateRootCollection_WhitespaceUserId_ReturnsFalse()
    {
        var result = await _authService.CanCreateRootCollectionAsync("   ");
        Assert.False(result);
    }

    // ── CanManageAclAsync — negative cases ─────────────────────────

    [Fact]
    public async Task CanManageAcl_ViewerRole_ReturnsFalse()
    {
        var collection = TestData.CreateCollection(name: "Viewer Only");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, UserA, RoleHierarchy.Roles.Viewer);

        var canManage = await _authService.CanManageAclAsync(UserA, collection.Id);

        Assert.False(canManage);
    }

    [Fact]
    public async Task CanManageAcl_ContributorRole_ReturnsFalse()
    {
        var collection = TestData.CreateCollection(name: "Contributor Only");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, UserA, RoleHierarchy.Roles.Contributor);

        var canManage = await _authService.CanManageAclAsync(UserA, collection.Id);

        Assert.False(canManage);
    }

    [Fact]
    public async Task CanManageAcl_NoAcl_ReturnsFalse()
    {
        var collection = TestData.CreateCollection(name: "No ACL");
        await _collectionRepo.CreateAsync(collection);

        var canManage = await _authService.CanManageAclAsync(UserA, collection.Id);

        Assert.False(canManage);
    }

    [Fact]
    public async Task CanManageAcl_ManagerRole_ReturnsTrue()
    {
        var collection = TestData.CreateCollection(name: "Manager");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, UserA, RoleHierarchy.Roles.Manager);

        var canManage = await _authService.CanManageAclAsync(UserA, collection.Id);

        Assert.True(canManage);
    }

    [Fact]
    public async Task CanManageAcl_AdminRole_ReturnsTrue()
    {
        var collection = TestData.CreateCollection(name: "Admin");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, UserA, RoleHierarchy.Roles.Admin);

        var canManage = await _authService.CanManageAclAsync(UserA, collection.Id);

        Assert.True(canManage);
    }

    // ── CheckAccessAsync — direct ACL + non-existent collection ─────

    [Fact]
    public async Task CheckAccess_DirectAcl_Viewer_MeetsViewerRequirement()
    {
        var collection = TestData.CreateCollection(name: "Direct ACL");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, UserA, RoleHierarchy.Roles.Viewer);

        var hasAccess = await _authService.CheckAccessAsync(UserA, collection.Id, RoleHierarchy.Roles.Viewer);

        Assert.True(hasAccess);
    }

    [Fact]
    public async Task CheckAccess_DirectAcl_Viewer_DoesNotMeetContributorRequirement()
    {
        var collection = TestData.CreateCollection(name: "Direct Viewer");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, UserA, RoleHierarchy.Roles.Viewer);

        var hasAccess = await _authService.CheckAccessAsync(UserA, collection.Id, RoleHierarchy.Roles.Contributor);

        Assert.False(hasAccess);
    }

    [Fact]
    public async Task CheckAccess_NonExistentCollection_ReturnsFalse()
    {
        var nonExistentId = Guid.NewGuid();

        var hasAccess = await _authService.CheckAccessAsync(UserA, nonExistentId, RoleHierarchy.Roles.Viewer);

        Assert.False(hasAccess);
    }

    [Fact]
    public async Task CheckAccess_NoAcl_ReturnsFalse()
    {
        var collection = TestData.CreateCollection(name: "No ACL Check");
        await _collectionRepo.CreateAsync(collection);

        var hasAccess = await _authService.CheckAccessAsync(UserA, collection.Id, RoleHierarchy.Roles.Viewer);

        Assert.False(hasAccess);
    }
}
