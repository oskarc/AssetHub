using Dam.Infrastructure.Data;
using Dam.Infrastructure.Repositories;
using Dam.Infrastructure.Services;
using Dam.Tests.Fixtures;
using Dam.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dam.Tests.EdgeCases;

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

    // ── Parent ACL propagates to child ──────────────────────────────

    [Fact]
    public async Task GetUserRole_ParentHasAcl_ChildInheritsRole()
    {
        // Arrange: parent with ACL, child with no ACL
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "contributor");

        // Act
        var role = await _authService.GetUserRoleAsync(User1, child.Id);

        // Assert
        Assert.Equal("contributor", role);
    }

    [Fact]
    public async Task GetUserRole_GrandparentHasAcl_GrandchildInheritsRole()
    {
        // Arrange: 3-level hierarchy, ACL only on root
        var root = TestData.CreateCollection(name: "Root");
        var mid = TestData.CreateCollection(name: "Mid", parentId: root.Id);
        var leaf = TestData.CreateCollection(name: "Leaf", parentId: mid.Id);
        await _collectionRepo.CreateAsync(root);
        await _collectionRepo.CreateAsync(mid);
        await _collectionRepo.CreateAsync(leaf);
        await _aclRepo.SetAccessAsync(root.Id, "user", User1, "manager");

        // Act
        var role = await _authService.GetUserRoleAsync(User1, leaf.Id);

        // Assert
        Assert.Equal("manager", role);
    }

    [Fact]
    public async Task CheckAccess_InheritedRole_MeetsRequirement()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "contributor");

        // Act — contributor meets viewer requirement
        var hasAccess = await _authService.CheckAccessAsync(User1, child.Id, "viewer");

        // Assert
        Assert.True(hasAccess);
    }

    [Fact]
    public async Task CheckAccess_InheritedRole_DoesNotExceedGrant()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "viewer");

        // Act — viewer does NOT meet manager requirement
        var hasAccess = await _authService.CheckAccessAsync(User1, child.Id, "manager");

        // Assert
        Assert.False(hasAccess);
    }

    // ── Explicit child ACL overrides inherited ──────────────────────

    [Fact]
    public async Task GetUserRole_DirectAclOnChild_ReturnsDirect_NotInherited()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "viewer");
        await _aclRepo.SetAccessAsync(child.Id, "user", User1, "manager");

        // Act
        var role = await _authService.GetUserRoleAsync(User1, child.Id);

        // Assert — direct ACL takes priority
        Assert.Equal("manager", role);
    }

    [Fact]
    public async Task IsRoleInherited_DirectAcl_ReturnsFalse()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "viewer");
        await _aclRepo.SetAccessAsync(child.Id, "user", User1, "manager");

        // Act
        var isInherited = await _authService.IsRoleInheritedAsync(User1, child.Id);

        // Assert
        Assert.False(isInherited);
    }

    [Fact]
    public async Task IsRoleInherited_NoDirectAcl_ReturnsTrue()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "contributor");

        // Act
        var isInherited = await _authService.IsRoleInheritedAsync(User1, child.Id);

        // Assert
        Assert.True(isInherited);
    }

    // ── No ACL anywhere → no access ────────────────────────────────

    [Fact]
    public async Task GetUserRole_NoAclAnywhere_ReturnsNull()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);

        // Act
        var role = await _authService.GetUserRoleAsync(User1, child.Id);

        // Assert
        Assert.Null(role);
    }

    [Fact]
    public async Task GetUserRole_OtherUserHasAcl_CurrentUserGetsNull()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User2, "admin");

        // Act — User1 has no ACL, User2 does
        var role = await _authService.GetUserRoleAsync(User1, child.Id);

        // Assert
        Assert.Null(role);
    }

    // ── GetAccessibleCollectionsAsync includes inherited ────────────

    [Fact]
    public async Task GetAccessibleCollections_IncludesChildrenOfAclCollection()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child1 = TestData.CreateCollection(name: "Child1", parentId: parent.Id);
        var child2 = TestData.CreateCollection(name: "Child2", parentId: parent.Id);
        var grandchild = TestData.CreateCollection(name: "Grandchild", parentId: child1.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child1);
        await _collectionRepo.CreateAsync(child2);
        await _collectionRepo.CreateAsync(grandchild);

        // Only ACL on parent
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "viewer");

        // Act
        var accessible = (await _collectionRepo.GetAccessibleCollectionsAsync(User1)).ToList();

        // Assert — should include parent + all descendants
        Assert.Equal(4, accessible.Count);
        Assert.Contains(accessible, c => c.Id == parent.Id);
        Assert.Contains(accessible, c => c.Id == child1.Id);
        Assert.Contains(accessible, c => c.Id == child2.Id);
        Assert.Contains(accessible, c => c.Id == grandchild.Id);
    }

    [Fact]
    public async Task GetAccessibleCollections_DoesNotIncludeUnrelatedCollections()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        var unrelated = TestData.CreateCollection(name: "Unrelated");
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _collectionRepo.CreateAsync(unrelated);

        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "contributor");

        // Act
        var accessible = (await _collectionRepo.GetAccessibleCollectionsAsync(User1)).ToList();

        // Assert
        Assert.Equal(2, accessible.Count);
        Assert.DoesNotContain(accessible, c => c.Id == unrelated.Id);
    }

    [Fact]
    public async Task GetAccessibleCollections_DirectAclOnChild_StillIncluded()
    {
        // A child with its own direct ACL should still be in the list
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);

        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "viewer");
        await _aclRepo.SetAccessAsync(child.Id, "user", User1, "manager");

        // Act
        var accessible = (await _collectionRepo.GetAccessibleCollectionsAsync(User1)).ToList();

        // Assert
        Assert.Equal(2, accessible.Count);
    }

    // ── Revoke parent → children lose inherited access ──────────────

    [Fact]
    public async Task RevokeParentAcl_ChildLosesInheritedAccess()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "contributor");

        // Verify access before revoke
        var roleBefore = await _authService.GetUserRoleAsync(User1, child.Id);
        Assert.Equal("contributor", roleBefore);

        // Revoke parent ACL
        await _aclRepo.RevokeAccessAsync(parent.Id, "user", User1);

        // Need a fresh auth service to clear the request-scoped cache
        var freshAuthService = new CollectionAuthorizationService(
            _db, NullLogger<CollectionAuthorizationService>.Instance);

        // Act
        var roleAfter = await freshAuthService.GetUserRoleAsync(User1, child.Id);

        // Assert
        Assert.Null(roleAfter);
    }

    // ── CanCreateSubCollection respects inheritance ─────────────────

    [Fact]
    public async Task CanCreateSubCollection_InheritedContributor_ReturnsTrue()
    {
        var root = TestData.CreateCollection(name: "Root");
        var child = TestData.CreateCollection(name: "Child", parentId: root.Id);
        await _collectionRepo.CreateAsync(root);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(root.Id, "user", User1, "contributor");

        // Act — user wants to create a sub-collection under child (inherited contributor should suffice)
        var canCreate = await _authService.CanCreateSubCollectionAsync(User1, child.Id);

        // Assert
        Assert.True(canCreate);
    }

    [Fact]
    public async Task CanManageAcl_InheritedManager_ReturnsTrue()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "manager");

        // Act
        var canManage = await _authService.CanManageAclAsync(User1, child.Id);

        // Assert
        Assert.True(canManage);
    }

    // ── Multiple users with different inherited roles ────────────────

    [Fact]
    public async Task MultipleUsers_DifferentInheritedRoles()
    {
        var parent = TestData.CreateCollection(name: "Parent");
        var child = TestData.CreateCollection(name: "Child", parentId: parent.Id);
        await _collectionRepo.CreateAsync(parent);
        await _collectionRepo.CreateAsync(child);
        await _aclRepo.SetAccessAsync(parent.Id, "user", User1, "viewer");
        await _aclRepo.SetAccessAsync(parent.Id, "user", User2, "admin");

        // Act
        var role1 = await _authService.GetUserRoleAsync(User1, child.Id);
        var role2 = await _authService.GetUserRoleAsync(User2, child.Id);

        // Assert
        Assert.Equal("viewer", role1);
        Assert.Equal("admin", role2);
    }

    // ── Deep hierarchy (5 levels) ───────────────────────────────────

    [Fact]
    public async Task GetUserRole_DeepHierarchy_InheritsFromRoot()
    {
        var l1 = TestData.CreateCollection(name: "L1");
        var l2 = TestData.CreateCollection(name: "L2", parentId: l1.Id);
        var l3 = TestData.CreateCollection(name: "L3", parentId: l2.Id);
        var l4 = TestData.CreateCollection(name: "L4", parentId: l3.Id);
        var l5 = TestData.CreateCollection(name: "L5", parentId: l4.Id);
        await _collectionRepo.CreateAsync(l1);
        await _collectionRepo.CreateAsync(l2);
        await _collectionRepo.CreateAsync(l3);
        await _collectionRepo.CreateAsync(l4);
        await _collectionRepo.CreateAsync(l5);
        await _aclRepo.SetAccessAsync(l1.Id, "user", User1, "manager");

        // Act
        var role = await _authService.GetUserRoleAsync(User1, l5.Id);

        // Assert
        Assert.Equal("manager", role);
    }

    [Fact]
    public async Task GetAccessibleCollections_DeepHierarchy_AllIncluded()
    {
        var l1 = TestData.CreateCollection(name: "L1");
        var l2 = TestData.CreateCollection(name: "L2", parentId: l1.Id);
        var l3 = TestData.CreateCollection(name: "L3", parentId: l2.Id);
        var l4 = TestData.CreateCollection(name: "L4", parentId: l3.Id);
        var l5 = TestData.CreateCollection(name: "L5", parentId: l4.Id);
        await _collectionRepo.CreateAsync(l1);
        await _collectionRepo.CreateAsync(l2);
        await _collectionRepo.CreateAsync(l3);
        await _collectionRepo.CreateAsync(l4);
        await _collectionRepo.CreateAsync(l5);
        await _aclRepo.SetAccessAsync(l1.Id, "user", User1, "viewer");

        // Act
        var accessible = (await _collectionRepo.GetAccessibleCollectionsAsync(User1)).ToList();

        // Assert
        Assert.Equal(5, accessible.Count);
    }
}
