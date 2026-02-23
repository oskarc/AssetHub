using AssetHub.Application;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.EdgeCases;

/// <summary>
/// Tests for collection ACL behavior with flat collections (no hierarchy).
/// </summary>
[Collection("Database")]
public class CollectionAclInheritanceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionRepository _collectionRepo = null!;
    private CollectionAclRepository _aclRepo = null!;
    private CollectionAuthorizationService _authService = null!;

    private const string User1 = "user-inherit-1";
    private const string User2 = "user-inherit-2";

    public CollectionAclInheritanceTests(PostgresFixture fixture) => _fixture = fixture;

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

    // ── Direct ACL behavior ─────────────────────────────────────────

    [Fact]
    public async Task GetUserRole_DirectAcl_ReturnsRole()
    {
        var collection = TestData.CreateCollection(name: "Collection");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, User1, RoleHierarchy.Roles.Contributor);

        var role = await _authService.GetUserRoleAsync(User1, collection.Id);

        Assert.Equal(RoleHierarchy.Roles.Contributor, role);
    }

    [Fact]
    public async Task GetUserRole_NoAcl_ReturnsNull()
    {
        var collection = TestData.CreateCollection(name: "Collection");
        await _collectionRepo.CreateAsync(collection);

        var role = await _authService.GetUserRoleAsync(User1, collection.Id);

        Assert.Null(role);
    }

    [Fact]
    public async Task GetUserRole_OtherUserHasAcl_CurrentUserGetsNull()
    {
        var collection = TestData.CreateCollection(name: "Collection");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, User2, RoleHierarchy.Roles.Admin);

        var role = await _authService.GetUserRoleAsync(User1, collection.Id);

        Assert.Null(role);
    }

    // ── GetAccessibleCollectionsAsync ───────────────────────────────

    [Fact]
    public async Task GetAccessibleCollections_ReturnsOnlyUserCollections()
    {
        var accessible = TestData.CreateCollection(name: "Accessible");
        var unrelated = TestData.CreateCollection(name: "Unrelated");
        await _collectionRepo.CreateAsync(accessible);
        await _collectionRepo.CreateAsync(unrelated);

        await _aclRepo.SetAccessAsync(accessible.Id, Constants.PrincipalTypes.User, User1, RoleHierarchy.Roles.Contributor);

        var result = (await _collectionRepo.GetAccessibleCollectionsAsync(User1)).ToList();

        Assert.Single(result);
        Assert.Equal(accessible.Id, result[0].Id);
    }

    // ── CanManageAcl ────────────────────────────────────────────────

    [Fact]
    public async Task CanManageAcl_Manager_ReturnsTrue()
    {
        var collection = TestData.CreateCollection(name: "Collection");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, User1, RoleHierarchy.Roles.Manager);

        var canManage = await _authService.CanManageAclAsync(User1, collection.Id);

        Assert.True(canManage);
    }

    // ── Multiple users with different roles ─────────────────────────

    [Fact]
    public async Task MultipleUsers_DifferentRoles()
    {
        var collection = TestData.CreateCollection(name: "Collection");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, User1, RoleHierarchy.Roles.Viewer);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, User2, RoleHierarchy.Roles.Admin);

        var role1 = await _authService.GetUserRoleAsync(User1, collection.Id);
        var role2 = await _authService.GetUserRoleAsync(User2, collection.Id);

        Assert.Equal(RoleHierarchy.Roles.Viewer, role1);
        Assert.Equal(RoleHierarchy.Roles.Admin, role2);
    }

    // ── Revoke ACL removes access ───────────────────────────────────

    [Fact]
    public async Task RevokeAcl_RemovesAccess()
    {
        var collection = TestData.CreateCollection(name: "Collection");
        await _collectionRepo.CreateAsync(collection);
        await _aclRepo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, User1, RoleHierarchy.Roles.Contributor);

        var roleBefore = await _authService.GetUserRoleAsync(User1, collection.Id);
        Assert.Equal(RoleHierarchy.Roles.Contributor, roleBefore);

        await _aclRepo.RevokeAccessAsync(collection.Id, Constants.PrincipalTypes.User, User1);

        var freshAuthService = new CollectionAuthorizationService(
            _db, NullLogger<CollectionAuthorizationService>.Instance);

        var roleAfter = await freshAuthService.GetUserRoleAsync(User1, collection.Id);

        Assert.Null(roleAfter);
    }
}

