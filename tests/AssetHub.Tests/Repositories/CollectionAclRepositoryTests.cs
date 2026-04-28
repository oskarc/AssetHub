using AssetHub.Application;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Repositories;

[Collection("Database")]
public class CollectionAclRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionAclRepository _repo = null!;

    public CollectionAclRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        _repo = new CollectionAclRepository(_fixture.CreateDbContextProvider(dbName), NullLogger<CollectionAclRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── GetByCollectionAsync ────────────────────────────────────────

    [Fact]
    public async Task GetByCollectionAsync_ReturnsAcls()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user2", AclRole.Contributor));
        await _db.SaveChangesAsync();

        var acls = (await _repo.GetByCollectionAsync(collection.Id)).ToList();

        Assert.Equal(2, acls.Count);
    }

    [Fact]
    public async Task GetByCollectionAsync_DoesNotReturnOtherCollections()
    {
        var col1 = TestData.CreateCollection(name: "C1");
        var col2 = TestData.CreateCollection(name: "C2");
        _db.Collections.AddRange(col1, col2);
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(col2.Id, "user2", AclRole.Viewer));
        await _db.SaveChangesAsync();

        var acls = (await _repo.GetByCollectionAsync(col1.Id)).ToList();

        Assert.Single(acls);
        Assert.Equal("user1", acls[0].PrincipalId);
    }

    // ── GetByPrincipalAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetByPrincipalAsync_ReturnsMatchingAcl()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Manager));
        await _db.SaveChangesAsync();

        var acl = await _repo.GetByPrincipalAsync(collection.Id, Constants.PrincipalTypes.User, "user1");

        Assert.NotNull(acl);
        Assert.Equal(AclRole.Manager, acl.Role);
    }

    [Fact]
    public async Task GetByPrincipalAsync_ReturnsNull_WhenNotFound()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var acl = await _repo.GetByPrincipalAsync(collection.Id, Constants.PrincipalTypes.User, "nonexistent");
        Assert.Null(acl);
    }

    // ── SetAccessAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SetAccessAsync_CreatesNewAcl()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var acl = await _repo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, "user1", RoleHierarchy.Roles.Contributor);

        Assert.NotNull(acl);
        Assert.Equal(AclRole.Contributor, acl.Role);
        Assert.Equal(collection.Id, acl.CollectionId);
    }

    [Fact]
    public async Task SetAccessAsync_UpdatesExistingAcl()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Viewer));
        await _db.SaveChangesAsync();

        var updated = await _repo.SetAccessAsync(collection.Id, Constants.PrincipalTypes.User, "user1", RoleHierarchy.Roles.Manager);

        Assert.Equal(AclRole.Manager, updated.Role);

        // Should still only be one ACL for this user+collection
        var allAcls = (await _repo.GetByCollectionAsync(collection.Id)).ToList();
        Assert.Single(allAcls);
    }

    // ── RevokeAccessAsync ───────────────────────────────────────────

    [Fact]
    public async Task RevokeAccessAsync_RemovesAcl()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Viewer));
        await _db.SaveChangesAsync();

        await _repo.RevokeAccessAsync(collection.Id, Constants.PrincipalTypes.User, "user1");

        var acl = await _repo.GetByPrincipalAsync(collection.Id, Constants.PrincipalTypes.User, "user1");
        Assert.Null(acl);
    }

    [Fact]
    public async Task RevokeAccessAsync_NoOp_WhenNotExists()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var ex = await Record.ExceptionAsync(() => _repo.RevokeAccessAsync(collection.Id, Constants.PrincipalTypes.User, "nonexistent"));
        Assert.Null(ex);

        var acls = await _repo.GetByCollectionAsync(collection.Id);
        Assert.Empty(acls);
    }

    // ── RevokeAllAccessAsync ────────────────────────────────────────

    [Fact]
    public async Task RevokeAllAccessAsync_RemovesAllAcls()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user2", AclRole.Contributor));
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user3", AclRole.Admin));
        await _db.SaveChangesAsync();

        await _repo.RevokeAllAccessAsync(collection.Id);

        var remaining = (await _repo.GetByCollectionAsync(collection.Id)).ToList();
        Assert.Empty(remaining);
    }

    // ── GetByUserAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByUserAsync_ReturnsUserAclsAcrossCollections()
    {
        var col1 = TestData.CreateCollection(name: "C1");
        var col2 = TestData.CreateCollection(name: "C2");
        _db.Collections.AddRange(col1, col2);
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(col2.Id, "user1", AclRole.Contributor));
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, "user2", AclRole.Admin));
        await _db.SaveChangesAsync();

        var acls = (await _repo.GetByUserAsync("user1")).ToList();

        Assert.Equal(2, acls.Count);
        Assert.All(acls, a => Assert.Equal("user1", a.PrincipalId));
    }

    // ── GetAllAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllAcls()
    {
        var col1 = TestData.CreateCollection(name: "C1");
        var col2 = TestData.CreateCollection(name: "C2");
        _db.Collections.AddRange(col1, col2);
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(col2.Id, "user2", AclRole.Admin));
        await _db.SaveChangesAsync();

        var all = (await _repo.GetAllAsync()).ToList();
        Assert.Equal(2, all.Count);
    }
}
