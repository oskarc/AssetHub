using AssetHub.Application;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.EdgeCases;

/// <summary>
/// Auth-path tests for nested collections (T5-NEST-01). Validates the
/// opt-in inheritance model: collections with <c>InheritParentAcl = true</c>
/// walk the parent chain looking for grants; the highest role wins; the
/// walk stops at the first ancestor with the flag off; collections that
/// haven't opted in take the same fast path as the flat ACL model.
/// </summary>
[Collection("Database")]
public class CollectionInheritanceWalkTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionRepository _collectionRepo = null!;
    private CollectionAclRepository _aclRepo = null!;

    private const string User = "user-1";

    public CollectionInheritanceWalkTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _collectionRepo = new CollectionRepository(_db, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
        _aclRepo = new CollectionAclRepository(_db, NullLogger<CollectionAclRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private CollectionAuthorizationService CreateAuth() =>
        new(_db, _collectionRepo, CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance);

    private async Task<Collection> CreateCollectionAsync(string name, Guid? parentId = null, bool inherit = false)
    {
        var c = new Collection
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedByUserId = "system",
            CreatedAt = DateTime.UtcNow,
            ParentCollectionId = parentId,
            InheritParentAcl = inherit,
        };
        _db.Collections.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task Flat_NoInheritance_ParentGrantDoesNotApplyToChild()
    {
        var parent = await CreateCollectionAsync("parent");
        var child = await CreateCollectionAsync("child", parent.Id, inherit: false);
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, User, RoleHierarchy.Roles.Viewer);

        var auth = CreateAuth();
        Assert.Equal(RoleHierarchy.Roles.Viewer, await auth.GetUserRoleAsync(User, parent.Id));
        Assert.Null(await auth.GetUserRoleAsync(User, child.Id));
    }

    [Fact]
    public async Task Inherit_True_ParentGrantAppliesToChild()
    {
        var parent = await CreateCollectionAsync("parent");
        var child = await CreateCollectionAsync("child", parent.Id, inherit: true);
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, User, RoleHierarchy.Roles.Viewer);

        var auth = CreateAuth();
        Assert.Equal(RoleHierarchy.Roles.Viewer, await auth.GetUserRoleAsync(User, child.Id));
        Assert.True(await auth.CheckAccessAsync(User, child.Id, RoleHierarchy.Roles.Viewer));
        Assert.False(await auth.CheckAccessAsync(User, child.Id, RoleHierarchy.Roles.Contributor));
    }

    [Fact]
    public async Task Inherit_True_HighestRoleWinsAcrossChain()
    {
        var parent = await CreateCollectionAsync("parent");
        var child = await CreateCollectionAsync("child", parent.Id, inherit: true);
        // Direct Viewer on child, Manager on parent → effective Manager on child.
        await _aclRepo.SetAccessAsync(child.Id, Constants.PrincipalTypes.User, User, RoleHierarchy.Roles.Viewer);
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, User, RoleHierarchy.Roles.Manager);

        var auth = CreateAuth();
        Assert.Equal(RoleHierarchy.Roles.Manager, await auth.GetUserRoleAsync(User, child.Id));
    }

    [Fact]
    public async Task Inherit_BreaksMidChain_AncestorsAboveBreakAreIgnored()
    {
        // A → B → C. C inherits from B, B does NOT inherit from A.
        // Granting Manager on A should NOT propagate to C; only B's ACL applies.
        var a = await CreateCollectionAsync("A");
        var b = await CreateCollectionAsync("B", a.Id, inherit: false);
        var c = await CreateCollectionAsync("C", b.Id, inherit: true);

        await _aclRepo.SetAccessAsync(a.Id, Constants.PrincipalTypes.User, User, RoleHierarchy.Roles.Manager);
        await _aclRepo.SetAccessAsync(b.Id, Constants.PrincipalTypes.User, User, RoleHierarchy.Roles.Viewer);

        var auth = CreateAuth();
        Assert.Equal(RoleHierarchy.Roles.Manager, await auth.GetUserRoleAsync(User, a.Id));
        Assert.Equal(RoleHierarchy.Roles.Viewer, await auth.GetUserRoleAsync(User, b.Id));
        Assert.Equal(RoleHierarchy.Roles.Viewer, await auth.GetUserRoleAsync(User, c.Id));
    }

    [Fact]
    public async Task Inherit_DepthCap_StopsAtMaxCollectionDepth()
    {
        // Build a chain longer than MaxCollectionDepth (8) and grant only on the root.
        // The deepest collection should NOT see the grant — the walk stops at the cap.
        var chain = new List<Collection> { await CreateCollectionAsync("0") };
        for (var i = 1; i < Constants.Limits.MaxCollectionDepth + 3; i++)
            chain.Add(await CreateCollectionAsync($"{i}", chain[^1].Id, inherit: true));

        await _aclRepo.SetAccessAsync(chain[0].Id, Constants.PrincipalTypes.User, User, RoleHierarchy.Roles.Manager);

        var auth = CreateAuth();
        // Within the depth cap (i.e. levels 0..MaxCollectionDepth) the grant
        // is reachable. Beyond it, the walk stops and the grant is invisible.
        Assert.Equal(RoleHierarchy.Roles.Manager, await auth.GetUserRoleAsync(User, chain[Constants.Limits.MaxCollectionDepth].Id));
        Assert.Null(await auth.GetUserRoleAsync(User, chain[^1].Id));
    }

    [Fact]
    public async Task FilterAccessibleAsync_RespectsInheritanceForBatch()
    {
        // Two siblings, one with inheritance, one without. The inheriting one
        // sees the parent's grant; the flat one doesn't.
        var parent = await CreateCollectionAsync("parent");
        var inheritingChild = await CreateCollectionAsync("inheriting", parent.Id, inherit: true);
        var flatChild = await CreateCollectionAsync("flat", parent.Id, inherit: false);
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, User, RoleHierarchy.Roles.Viewer);

        var auth = CreateAuth();
        var accessible = await auth.FilterAccessibleAsync(
            User,
            new[] { parent.Id, inheritingChild.Id, flatChild.Id },
            RoleHierarchy.Roles.Viewer);

        Assert.Contains(parent.Id, accessible);
        Assert.Contains(inheritingChild.Id, accessible);
        Assert.DoesNotContain(flatChild.Id, accessible);
    }

    [Fact]
    public async Task CollectionWithoutAnyAncestor_NoGrant_ReturnsNull()
    {
        var solo = await CreateCollectionAsync("solo");

        var auth = CreateAuth();
        Assert.Null(await auth.GetUserRoleAsync(User, solo.Id));
        Assert.False(await auth.CheckAccessAsync(User, solo.Id, RoleHierarchy.Roles.Viewer));
    }

    [Fact]
    public async Task GetInheritingDescendantIds_OnlyFollowsInheritingLinks()
    {
        var parent = await CreateCollectionAsync("parent");
        var inheriting = await CreateCollectionAsync("inheriting", parent.Id, inherit: true);
        var flat = await CreateCollectionAsync("flat", parent.Id, inherit: false);
        // Grandchild via the inheriting branch — flag-on, should be included.
        var grandchild = await CreateCollectionAsync("grandchild", inheriting.Id, inherit: true);
        // Grandchild via the flat branch — flag-off, should NOT be included.
        await CreateCollectionAsync("flat-gc", flat.Id, inherit: false);

        var ids = await _collectionRepo.GetInheritingDescendantIdsAsync(parent.Id);

        Assert.Contains(inheriting.Id, ids);
        Assert.Contains(grandchild.Id, ids);
        Assert.DoesNotContain(flat.Id, ids);
    }
}
