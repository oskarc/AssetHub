using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using UserTuple = (string Id, string Username, string? Email, string? FirstName, string? LastName, System.DateTime? CreatedAt);

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for CollectionAclService — role escalation guards, ACL CRUD, user search.
/// </summary>
[Collection("Database")]
public class CollectionAclServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionRepository _collectionRepo = null!;
    private CollectionAclRepository _aclRepo = null!;
    private CollectionAuthorizationService _authService = null!;
    private Mock<IUserLookupService> _userLookupMock = null!;
    private Mock<IKeycloakUserService> _keycloakMock = null!;
    private Mock<IAuditService> _auditMock = null!;

    private const string AdminUser = "acl-admin-001";
    private const string ManagerUser = "acl-manager-001";
    private const string ViewerUser = "acl-viewer-001";
    private const string NewUser = "acl-new-user-001";

    public CollectionAclServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _collectionRepo = new CollectionRepository(_db, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
        _aclRepo = new CollectionAclRepository(_db, NullLogger<CollectionAclRepository>.Instance);
        _authService = new CollectionAuthorizationService(_db, CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance);
        _userLookupMock = new Mock<IUserLookupService>();
        _keycloakMock = new Mock<IKeycloakUserService>();
        _auditMock = new Mock<IAuditService>();

        // Default mock setups
        _userLookupMock.Setup(x => x.GetUserNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _userLookupMock.Setup(x => x.GetUserEmailsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _keycloakMock.Setup(x => x.GetRealmRoleMemberIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
    }

    private CollectionAclService CreateService(string userId, bool isAdmin = false)
    {
        var currentUser = new CurrentUser(userId, isAdmin);

        return new CollectionAclService(
            new CollectionAclRepositories(_collectionRepo, _aclRepo), _authService, _userLookupMock.Object,
            _keycloakMock.Object, _auditMock.Object, TestCacheHelper.CreateHybridCache(), currentUser);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── SetAccessAsync (Manager self-service) ───────────────────────

    [Fact]
    public async Task SetAccess_ManagerCanGrantViewer_Success()
    {
        var col = TestData.CreateCollection(name: "Managed");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        var result = await svc.SetAccessAsync(col.Id, Constants.PrincipalTypes.User, NewUser, RoleHierarchy.Roles.Viewer, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RoleHierarchy.Roles.Viewer, result.Value!.Role);
        Assert.Equal(NewUser, result.Value.PrincipalId);
    }

    [Fact]
    public async Task SetAccess_ManagerCanGrantContributor_Success()
    {
        var col = TestData.CreateCollection(name: "Managed");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        var result = await svc.SetAccessAsync(col.Id, Constants.PrincipalTypes.User, NewUser, RoleHierarchy.Roles.Contributor, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RoleHierarchy.Roles.Contributor, result.Value!.Role);
    }

    [Fact]
    public async Task SetAccess_ManagerCannotGrantAdmin_ReturnsBadRequest()
    {
        var col = TestData.CreateCollection(name: "Managed");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        var result = await svc.SetAccessAsync(col.Id, Constants.PrincipalTypes.User, NewUser, RoleHierarchy.Roles.Admin, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("exceeds", result.Error.Message);
    }

    [Fact]
    public async Task SetAccess_ViewerCannotManageAcl_ReturnsForbidden()
    {
        var col = TestData.CreateCollection(name: "ReadOnly");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ViewerUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var svc = CreateService(ViewerUser);
        var result = await svc.SetAccessAsync(col.Id, Constants.PrincipalTypes.User, NewUser, RoleHierarchy.Roles.Viewer, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetAccess_InvalidRole_ReturnsBadRequest()
    {
        var col = TestData.CreateCollection(name: "Managed");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        var result = await svc.SetAccessAsync(col.Id, Constants.PrincipalTypes.User, NewUser, "superadmin", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetAccess_LogsAuditEvent()
    {
        var col = TestData.CreateCollection(name: "Audited");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        await svc.SetAccessAsync(col.Id, Constants.PrincipalTypes.User, NewUser, RoleHierarchy.Roles.Viewer, CancellationToken.None);

        _auditMock.Verify(a => a.LogAsync(
            "acl.set", Constants.ScopeTypes.Collection, col.Id, ManagerUser,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── RevokeAccessAsync (Manager self-service) ────────────────────

    [Fact]
    public async Task RevokeAccess_ManagerCanRevokeViewer_Success()
    {
        var col = TestData.CreateCollection(name: "Managed");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ViewerUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        var result = await svc.RevokeAccessAsync(col.Id, Constants.PrincipalTypes.User, ViewerUser, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Verify ACL actually removed
        var acl = await _aclRepo.GetByPrincipalAsync(col.Id, Constants.PrincipalTypes.User, ViewerUser);
        Assert.Null(acl);
    }

    [Fact]
    public async Task RevokeAccess_ManagerCannotRevokeAdmin_ReturnsBadRequest()
    {
        var col = TestData.CreateCollection(name: "AdminProtected");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        var result = await svc.RevokeAccessAsync(col.Id, Constants.PrincipalTypes.User, AdminUser, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("exceeds", result.Error.Message);
    }

    // ── GetAclsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetAcls_ManagerCanViewAcls()
    {
        var col = TestData.CreateCollection(name: "WithAcls");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ViewerUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var svc = CreateService(ManagerUser);
        var result = await svc.GetAclsAsync(col.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count());
    }

    [Fact]
    public async Task GetAcls_ViewerCannotViewAcls_ReturnsForbidden()
    {
        var col = TestData.CreateCollection(name: "Protected");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ViewerUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var svc = CreateService(ViewerUser);
        var result = await svc.GetAclsAsync(col.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    // ── AdminSetAccessAsync ─────────────────────────────────────────

    [Fact]
    public async Task AdminSetAccess_NonExistentCollection_ReturnsNotFound()
    {
        var svc = CreateService(AdminUser, isAdmin: true);
        var request = new SetCollectionAccessRequest { PrincipalId = NewUser, Role = RoleHierarchy.Roles.Viewer };

        var result = await svc.AdminSetAccessAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task AdminSetAccess_ValidCollection_GrantsAccess()
    {
        var col = TestData.CreateCollection(name: "AdminManaged");
        _db.Collections.Add(col);
        await _db.SaveChangesAsync();

        // NewUser is not a GUID, so the service resolves by username
        var resolvedId = Guid.NewGuid().ToString();
        _userLookupMock.Setup(x => x.GetUserIdByUsernameAsync(NewUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedId);

        var svc = CreateService(AdminUser, isAdmin: true);
        var request = new SetCollectionAccessRequest { PrincipalId = NewUser, Role = RoleHierarchy.Roles.Contributor };

        var result = await svc.AdminSetAccessAsync(col.Id, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RoleHierarchy.Roles.Contributor, result.Value!.Role);
    }

    // ── AdminRevokeAccessAsync ──────────────────────────────────────

    [Fact]
    public async Task AdminRevokeAccess_Success()
    {
        var col = TestData.CreateCollection(name: "AdminRevokeTarget");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ViewerUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var svc = CreateService(AdminUser, isAdmin: true);
        var result = await svc.AdminRevokeAccessAsync(col.Id, Constants.PrincipalTypes.User, ViewerUser, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Verify removed
        var acl = await _aclRepo.GetByPrincipalAsync(col.Id, Constants.PrincipalTypes.User, ViewerUser);
        Assert.Null(acl);
    }

    [Fact]
    public async Task AdminRevokeAccess_NonExistentCollection_ReturnsNotFound()
    {
        var svc = CreateService(AdminUser, isAdmin: true);
        var result = await svc.AdminRevokeAccessAsync(Guid.NewGuid(), Constants.PrincipalTypes.User, ViewerUser, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    // ── SearchUsersForAclAsync ──────────────────────────────────────

    [Fact]
    public async Task SearchUsersForAcl_ExcludesExistingAclUsers()
    {
        var col = TestData.CreateCollection(name: "SearchTarget");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        _userLookupMock.Setup(x => x.GetAllUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserTuple>
            {
                (ManagerUser, "manager", "m@test.com", null, null, null),
                (NewUser, "newuser", "n@test.com", null, null, null)
            });

        var svc = CreateService(ManagerUser);
        var result = await svc.SearchUsersForAclAsync(col.Id, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(NewUser, result.Value![0].Id);
    }

    [Fact]
    public async Task SearchUsersForAcl_FiltersbyQuery()
    {
        var col = TestData.CreateCollection(name: "SearchTarget2");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        _userLookupMock.Setup(x => x.SearchUsersAsync("alice", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string Id, string Username, string? Email)>
            {
                ("u1", "alice", "alice@test.com")
            });

        var svc = CreateService(ManagerUser);
        var result = await svc.SearchUsersForAclAsync(col.Id, "alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("alice", result.Value![0].Username);
    }
}
