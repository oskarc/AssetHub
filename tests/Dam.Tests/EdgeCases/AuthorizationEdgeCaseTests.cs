using Dam.Infrastructure.Data;
using Dam.Infrastructure.Repositories;
using Dam.Infrastructure.Services;
using Dam.Tests.Fixtures;
using Dam.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dam.Tests.EdgeCases;

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
    private const string UserB = "auth-edge-user-b";
    private const string UserC = "auth-edge-user-c";

    public AuthorizationEdgeCaseTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _collectionRepo = new CollectionRepository(_db);
        _aclRepo = new CollectionAclRepository(_db);
        _authService = new CollectionAuthorizationService(
            _db, NullLogger<CollectionAuthorizationService>.Instance);
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
        await _aclRepo.SetAccessAsync(collection.Id, "user", UserA, "viewer");

        var canManage = await _authService.CanManageAclAsync(UserA, collection.Id);

        Assert.False(canManage);
    }

    [Fact]
    public async Task CanManageAcl_ContributorRole_ReturnsFalse()
    {
        var collection = TestData.CreateCollection(name: "Contributor Only");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, "user", UserA, "contributor");

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
        await _aclRepo.SetAccessAsync(collection.Id, "user", UserA, "manager");

        var canManage = await _authService.CanManageAclAsync(UserA, collection.Id);

        Assert.True(canManage);
    }

    [Fact]
    public async Task CanManageAcl_AdminRole_ReturnsTrue()
    {
        var collection = TestData.CreateCollection(name: "Admin");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, "user", UserA, "admin");

        var canManage = await _authService.CanManageAclAsync(UserA, collection.Id);

        Assert.True(canManage);
    }

    // ── CanCreateSubCollectionAsync — negative cases ────────────────

    [Fact]
    public async Task CanCreateSubCollection_ViewerRole_ReturnsFalse()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        await _collectionRepo.CreateAsync(parent);
        await _aclRepo.SetAccessAsync(parent.Id, "user", UserA, "viewer");

        var canCreate = await _authService.CanCreateSubCollectionAsync(UserA, parent.Id);

        Assert.False(canCreate);
    }

    [Fact]
    public async Task CanCreateSubCollection_NoAcl_ReturnsFalse()
    {
        var parent = TestData.CreateCollection(name: "No ACL Parent");
        await _collectionRepo.CreateAsync(parent);

        var canCreate = await _authService.CanCreateSubCollectionAsync(UserA, parent.Id);

        Assert.False(canCreate);
    }

    [Fact]
    public async Task CanCreateSubCollection_ContributorRole_ReturnsTrue()
    {
        var parent = TestData.CreateCollection(name: "Contributor Parent");
        await _collectionRepo.CreateAsync(parent);
        await _aclRepo.SetAccessAsync(parent.Id, "user", UserA, "contributor");

        var canCreate = await _authService.CanCreateSubCollectionAsync(UserA, parent.Id);

        Assert.True(canCreate);
    }

    // ── CheckAccessAsync — direct ACL + non-existent collection ─────

    [Fact]
    public async Task CheckAccess_DirectAcl_Viewer_MeetsViewerRequirement()
    {
        var collection = TestData.CreateCollection(name: "Direct ACL");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, "user", UserA, "viewer");

        var hasAccess = await _authService.CheckAccessAsync(UserA, collection.Id, "viewer");

        Assert.True(hasAccess);
    }

    [Fact]
    public async Task CheckAccess_DirectAcl_Viewer_DoesNotMeetContributorRequirement()
    {
        var collection = TestData.CreateCollection(name: "Direct Viewer");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, "user", UserA, "viewer");

        var hasAccess = await _authService.CheckAccessAsync(UserA, collection.Id, "contributor");

        Assert.False(hasAccess);
    }

    [Fact]
    public async Task CheckAccess_NonExistentCollection_ReturnsFalse()
    {
        var nonExistentId = Guid.NewGuid();

        var hasAccess = await _authService.CheckAccessAsync(UserA, nonExistentId, "viewer");

        Assert.False(hasAccess);
    }

    [Fact]
    public async Task CheckAccess_NoAcl_ReturnsFalse()
    {
        var collection = TestData.CreateCollection(name: "No ACL Check");
        await _collectionRepo.CreateAsync(collection);

        var hasAccess = await _authService.CheckAccessAsync(UserA, collection.Id, "viewer");

        Assert.False(hasAccess);
    }

    // ── GetUserRoleAsync — multiple users on same chain ─────────────

    [Fact]
    public async Task GetUserRole_ThreeUsersOnSameChain_IndependentRoles()
    {
        var parent = TestData.CreateCollection(name: "Root");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);

        // UserA: admin on parent (inherits to child)
        await _aclRepo.SetAccessAsync(parent.Id, "user", UserA, "admin");
        // UserB: viewer on parent, contributor on child (direct override)
        await _aclRepo.SetAccessAsync(parent.Id, "user", UserB, "viewer");
        await _aclRepo.SetAccessAsync(child.Id, "user", UserB, "contributor");
        // UserC: no ACL at all
        // (no call needed)

        var roleA = await _authService.GetUserRoleAsync(UserA, child.Id);
        var roleB = await _authService.GetUserRoleAsync(UserB, child.Id);
        var roleC = await _authService.GetUserRoleAsync(UserC, child.Id);

        // UserA inherits admin from parent
        Assert.Equal("admin", roleA);
        // UserB has direct contributor on child (overrides viewer from parent)
        Assert.Equal("contributor", roleB);
        // UserC has no access
        Assert.Null(roleC);
    }

    [Fact]
    public async Task GetUserRole_DirectAclTakesPriority_EvenIfLowerThanInherited()
    {
        var parent = TestData.CreateCollection(name: "HighParent");
        var child = TestData.CreateCollection(name: "LowChild", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);

        // Admin on parent, but viewer on child directly
        await _aclRepo.SetAccessAsync(parent.Id, "user", UserA, "admin");
        await _aclRepo.SetAccessAsync(child.Id, "user", UserA, "viewer");

        var role = await _authService.GetUserRoleAsync(UserA, child.Id);

        // Direct ACL should take priority over inherited
        Assert.Equal("viewer", role);
    }
}
