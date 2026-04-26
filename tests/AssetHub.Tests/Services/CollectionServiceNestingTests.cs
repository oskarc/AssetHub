using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for the T5-NEST-01 mutations on <see cref="CollectionService"/>:
/// SetParentAsync (with cycle + depth guards), SetInheritParentAclAsync, and
/// CopyParentAclAsync. Each test seeds rows directly via the DbContext to keep
/// arrange-time minimal.
/// </summary>
[Collection("Database")]
public class CollectionServiceNestingTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionRepository _collectionRepo = null!;
    private CollectionAclRepository _aclRepo = null!;
    private ShareRepository _shareRepo = null!;
    private CollectionAuthorizationService _authService = null!;
    private Mock<IAuditService> _auditMock = null!;

    private const string Admin = "admin-1";

    public CollectionServiceNestingTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _collectionRepo = new CollectionRepository(_db, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
        _aclRepo = new CollectionAclRepository(_db, NullLogger<CollectionAclRepository>.Instance);
        _shareRepo = new ShareRepository(_db, NullLogger<ShareRepository>.Instance);
        _authService = new CollectionAuthorizationService(_db, _collectionRepo, CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance);
        _auditMock = new Mock<IAuditService>();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private CollectionService CreateService(bool isAdmin = true)
    {
        var currentUser = new CurrentUser(Admin, isAdmin);
        var minio = Options.Create(new MinIOSettings { BucketName = "test" });
        var repos = new CollectionServiceRepositories(_collectionRepo, _aclRepo, _shareRepo, TestCacheHelper.CreateHybridCache());
        return new CollectionService(
            repos,
            _authService,
            new Mock<IAssetDeletionService>().Object,
            new Mock<IZipBuildService>().Object,
            _auditMock.Object,
            new UnitOfWork(_db),
            minio,
            currentUser);
    }

    private async Task<Collection> CreateAsync(string name, Guid? parentId = null, bool inherit = false)
    {
        var c = new Collection
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedByUserId = Admin,
            CreatedAt = DateTime.UtcNow,
            ParentCollectionId = parentId,
            InheritParentAcl = inherit,
        };
        _db.Collections.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    // ── SetParentAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task SetParent_NonAdmin_Forbidden()
    {
        var c = await CreateAsync("c");
        var result = await CreateService(isAdmin: false).SetParentAsync(c.Id, null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetParent_HappyPath_PersistsAndAudits()
    {
        var parent = await CreateAsync("parent");
        var child = await CreateAsync("child");

        var result = await CreateService().SetParentAsync(child.Id, parent.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await _db.Collections.FindAsync(child.Id);
        Assert.Equal(parent.Id, reloaded!.ParentCollectionId);
        _auditMock.Verify(
            a => a.LogAsync("collection.reparented", "collection", child.Id, Admin,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetParent_SameParent_NoOpAndNoAudit()
    {
        var parent = await CreateAsync("parent");
        var child = await CreateAsync("child", parent.Id);

        var result = await CreateService().SetParentAsync(child.Id, parent.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _auditMock.Verify(
            a => a.LogAsync("collection.reparented", It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetParent_SelfCycle_Rejected()
    {
        var c = await CreateAsync("c");
        var result = await CreateService().SetParentAsync(c.Id, c.Id, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetParent_TransitiveCycle_Rejected()
    {
        // A → B → C. Trying to make A a child of C creates a cycle.
        var a = await CreateAsync("A");
        var b = await CreateAsync("B", a.Id);
        var c = await CreateAsync("C", b.Id);

        var result = await CreateService().SetParentAsync(a.Id, c.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("Cycle", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetParent_DepthExceeded_Rejected()
    {
        // Build a chain at the depth cap, then try to extend it by one.
        var root = await CreateAsync("root");
        var current = root;
        for (var i = 1; i < Constants.Limits.MaxCollectionDepth; i++)
            current = await CreateAsync($"d{i}", current.Id);

        // Now `current` sits exactly at the cap. Adding a parent above the
        // root would push the existing chain past the limit.
        var aboveRoot = await CreateAsync("above-root");
        var result = await CreateService().SetParentAsync(root.Id, aboveRoot.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetParent_UnknownParent_NotFound()
    {
        var c = await CreateAsync("c");
        var result = await CreateService().SetParentAsync(c.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetParent_UnknownCollection_NotFound()
    {
        var result = await CreateService().SetParentAsync(Guid.NewGuid(), null, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    // ── SetInheritParentAclAsync ───────────────────────────────────────

    [Fact]
    public async Task SetInherit_True_NoParent_Rejected()
    {
        var c = await CreateAsync("orphan");
        var result = await CreateService().SetInheritParentAclAsync(c.Id, true, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetInherit_True_WithParent_PersistsAndAudits()
    {
        var parent = await CreateAsync("parent");
        var child = await CreateAsync("child", parent.Id);

        var result = await CreateService().SetInheritParentAclAsync(child.Id, true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await _db.Collections.FindAsync(child.Id);
        Assert.True(reloaded!.InheritParentAcl);
        _auditMock.Verify(
            a => a.LogAsync("collection.inheritance_enabled", "collection", child.Id, Admin,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetInherit_False_DisablesAndAudits()
    {
        var parent = await CreateAsync("parent");
        var child = await CreateAsync("child", parent.Id, inherit: true);

        var result = await CreateService().SetInheritParentAclAsync(child.Id, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reloaded = await _db.Collections.FindAsync(child.Id);
        Assert.False(reloaded!.InheritParentAcl);
        _auditMock.Verify(
            a => a.LogAsync("collection.inheritance_disabled", "collection", child.Id, Admin,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetInherit_NoChange_NoAudit()
    {
        var parent = await CreateAsync("parent");
        var child = await CreateAsync("child", parent.Id, inherit: true);

        var result = await CreateService().SetInheritParentAclAsync(child.Id, true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _auditMock.Verify(
            a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── CopyParentAclAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CopyAcl_NoParent_Rejected()
    {
        var c = await CreateAsync("orphan");
        var result = await CreateService().CopyParentAclAsync(c.Id, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CopyAcl_HappyPath_AddsMissingPrincipals_DoesNotEnableInheritance()
    {
        var parent = await CreateAsync("parent");
        var child = await CreateAsync("child", parent.Id);
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, "u1", RoleHierarchy.Roles.Viewer);
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, "u2", RoleHierarchy.Roles.Manager);

        var result = await CreateService().CopyParentAclAsync(child.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        var childAcls = (await _aclRepo.GetByCollectionAsync(child.Id)).ToList();
        Assert.Equal(2, childAcls.Count);
        // Crucially: did NOT enable live inheritance — copy is a snapshot.
        var reloaded = await _db.Collections.FindAsync(child.Id);
        Assert.False(reloaded!.InheritParentAcl);
        _auditMock.Verify(
            a => a.LogAsync("collection.acl_copied_from_parent", "collection", child.Id, Admin,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CopyAcl_PartialOverlap_OnlyAddsMissingEntries()
    {
        var parent = await CreateAsync("parent");
        var child = await CreateAsync("child", parent.Id);
        // Both parent and child grant u1; only parent grants u2.
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, "u1", RoleHierarchy.Roles.Viewer);
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, "u2", RoleHierarchy.Roles.Manager);
        await _aclRepo.SetAccessAsync(child.Id, Constants.PrincipalTypes.User, "u1", RoleHierarchy.Roles.Contributor);

        var result = await CreateService().CopyParentAclAsync(child.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value); // only u2 was added
        var childAcls = (await _aclRepo.GetByCollectionAsync(child.Id)).ToList();
        Assert.Equal(2, childAcls.Count);
        // u1's existing Contributor wasn't overwritten by the parent's Viewer grant.
        var u1 = childAcls.First(a => a.PrincipalId == "u1");
        Assert.Equal(AclRole.Contributor, u1.Role);
    }

    [Fact]
    public async Task CopyAcl_NothingToCopy_ReturnsZero()
    {
        var parent = await CreateAsync("parent");
        var child = await CreateAsync("child", parent.Id);
        // Both grant the same principal already.
        await _aclRepo.SetAccessAsync(parent.Id, Constants.PrincipalTypes.User, "u1", RoleHierarchy.Roles.Viewer);
        await _aclRepo.SetAccessAsync(child.Id, Constants.PrincipalTypes.User, "u1", RoleHierarchy.Roles.Viewer);

        var result = await CreateService().CopyParentAclAsync(child.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }
}
