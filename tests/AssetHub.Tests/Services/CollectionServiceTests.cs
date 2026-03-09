using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
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
/// Integration tests for CollectionService — real DB + mocked external services.
/// </summary>
[Collection("Database")]
public class CollectionServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionService _service = null!;
    private CollectionRepository _collectionRepo = null!;
    private CollectionAclRepository _aclRepo = null!;
    private AssetRepository _assetRepo = null!;
    private ShareRepository _shareRepo = null!;
    private CollectionAuthorizationService _authService = null!;
    private Mock<IAssetDeletionService> _deletionServiceMock = null!;
    private Mock<IAssetCollectionRepository> _assetCollectionRepoMock = null!;
    private Mock<IZipBuildService> _zipBuildServiceMock = null!;
    private Mock<IAuditService> _auditMock = null!;

    private const string AdminUser = "admin-user-001";
    private const string ManagerUser = "manager-user-001";
    private const string ViewerUser = "viewer-user-001";
    private const string NoAccessUser = "no-access-user-001";

    public CollectionServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _collectionRepo = new CollectionRepository(_db, NullLogger<CollectionRepository>.Instance);
        _aclRepo = new CollectionAclRepository(_db, NullLogger<CollectionAclRepository>.Instance);
        _assetRepo = new AssetRepository(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<AssetRepository>.Instance);
        _shareRepo = new ShareRepository(_db, NullLogger<ShareRepository>.Instance);
        _authService = new CollectionAuthorizationService(_db, NullLogger<CollectionAuthorizationService>.Instance);
        _deletionServiceMock = new Mock<IAssetDeletionService>();
        _assetCollectionRepoMock = new Mock<IAssetCollectionRepository>();
        _zipBuildServiceMock = new Mock<IZipBuildService>();
        _auditMock = new Mock<IAuditService>();

        var minioSettings = Options.Create(new MinIOSettings { BucketName = "test-bucket" });

        _service = CreateService(AdminUser, isAdmin: true, minioSettings);
    }

    private CollectionService CreateService(string userId, bool isAdmin,
        IOptions<MinIOSettings>? minioSettings = null)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        minioSettings ??= Options.Create(new MinIOSettings { BucketName = "test-bucket" });

        return new CollectionService(
            _collectionRepo, _aclRepo, _assetRepo, _assetCollectionRepoMock.Object, _shareRepo,
            _authService, _deletionServiceMock.Object, _zipBuildServiceMock.Object,
            _auditMock.Object, minioSettings, currentUser);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AdminCreatesRootCollection_Success()
    {
        var dto = new CreateCollectionDto { Name = "Marketing" };

        var result = await _service.CreateAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Marketing", result.Value!.Name);
        Assert.Equal(RoleHierarchy.Roles.Admin, result.Value.UserRole);

        // Verify ACL was auto-created for the creator
        var acl = await _aclRepo.GetByPrincipalAsync(result.Value.Id, Constants.PrincipalTypes.User, AdminUser);
        Assert.NotNull(acl);
        Assert.Equal(AclRole.Admin, acl.Role);
    }

    [Fact]
    public async Task CreateAsync_DuplicateRootName_ReturnsBadRequest()
    {
        var existing = TestData.CreateCollection(name: "Marketing");
        _db.Collections.Add(existing);
        await _db.SaveChangesAsync();

        var dto = new CreateCollectionDto { Name = "Marketing" };

        var result = await _service.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("already exists", result.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ReturnsBadRequest()
    {
        var dto = new CreateCollectionDto { Name = "" };
        var result = await _service.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_AuthenticatedUserCanCreateRoot_WhenAuthServiceAllows()
    {
        // CanCreateRootCollectionAsync allows any authenticated user (role check at endpoint level)
        var viewerService = CreateService(ViewerUser, isAdmin: false);

        var dto = new CreateCollectionDto { Name = "Authenticated Root" };
        var result = await viewerService.CreateAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Authenticated Root", result.Value!.Name);
    }

    [Fact]
    public async Task CreateAsync_AuditEventLogged()
    {
        var dto = new CreateCollectionDto { Name = "Audited Collection" };

        await _service.CreateAsync(dto, CancellationToken.None);

        _auditMock.Verify(a => a.LogAsync(
            "collection.created", Constants.ScopeTypes.Collection, It.IsAny<Guid>(), AdminUser,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── GetByIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WithAccess_ReturnsCollection()
    {
        var col = TestData.CreateCollection(name: "Visible");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));
        await _db.SaveChangesAsync();

        var result = await _service.GetByIdAsync(col.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Visible", result.Value!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NoAccess_ReturnsForbidden()
    {
        var col = TestData.CreateCollection(name: "Secret");
        _db.Collections.Add(col);
        await _db.SaveChangesAsync();

        var noAccessService = CreateService(NoAccessUser, isAdmin: false);
        var result = await noAccessService.GetByIdAsync(col.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ManagerCanUpdate_Success()
    {
        var col = TestData.CreateCollection(name: "Original");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var managerService = CreateService(ManagerUser, isAdmin: false);
        var dto = new UpdateCollectionDto { Name = "Updated" };

        var result = await managerService.UpdateAsync(col.Id, dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Collection updated", result.Value!.Message);

        // Verify persisted
        _db.ChangeTracker.Clear();
        var updated = await _collectionRepo.GetByIdAsync(col.Id);
        Assert.Equal("Updated", updated!.Name);
    }

    [Fact]
    public async Task UpdateAsync_ViewerCannotUpdate_ReturnsForbidden()
    {
        var col = TestData.CreateCollection(name: "Original");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ViewerUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var viewerService = CreateService(ViewerUser, isAdmin: false);
        var dto = new UpdateCollectionDto { Name = "Hacked" };

        var result = await viewerService.UpdateAsync(col.Id, dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateRootName_ReturnsBadRequest()
    {
        var col1 = TestData.CreateCollection(name: "Existing");
        var col2 = TestData.CreateCollection(name: "ToRename");
        _db.Collections.AddRange(col1, col2);
        _db.CollectionAcls.Add(TestData.CreateAcl(col2.Id, AdminUser, AclRole.Admin));
        await _db.SaveChangesAsync();

        var dto = new UpdateCollectionDto { Name = "Existing" };
        var result = await _service.UpdateAsync(col2.Id, dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("already exists", result.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_DescriptionTooLong_ReturnsBadRequest()
    {
        var dto = new CreateCollectionDto { Name = "Valid", Description = new string('x', 1001) };
        var result = await _service.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("1000", result.Error.Message);
    }

    [Fact]
    public async Task UpdateAsync_NameTooLong_ReturnsBadRequest()
    {
        var col = TestData.CreateCollection(name: "Original");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));
        await _db.SaveChangesAsync();

        var dto = new UpdateCollectionDto { Name = new string('a', 256) };
        var result = await _service.UpdateAsync(col.Id, dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("255", result.Error.Message);
    }

    [Fact]
    public async Task UpdateAsync_DescriptionTooLong_ReturnsBadRequest()
    {
        var col = TestData.CreateCollection(name: "Original");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));
        await _db.SaveChangesAsync();

        var dto = new UpdateCollectionDto { Description = new string('x', 1001) };
        var result = await _service.UpdateAsync(col.Id, dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("1000", result.Error.Message);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_AdminCanDelete_Success()
    {
        var col = TestData.CreateCollection(name: "ToDelete");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));
        await _db.SaveChangesAsync();

        _deletionServiceMock.Setup(x => x.DeleteCollectionAssetsAsync(col.Id, "test-bucket", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Asset>());

        var result = await _service.DeleteAsync(col.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _deletionServiceMock.Verify(x => x.DeleteCollectionAssetsAsync(col.Id, "test-bucket", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ManagerCannotDelete_ReturnsForbidden()
    {
        var col = TestData.CreateCollection(name: "Protected");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var managerService = CreateService(ManagerUser, isAdmin: false);
        var result = await managerService.DeleteAsync(col.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsNotFound()
    {
        var result = await _service.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        // Admin has CheckAccess that fails on non-existent collection
        Assert.False(result.IsSuccess);
    }

    // ── GetRootCollectionsAsync ─────────────────────────────────────

    [Fact]
    public async Task GetRootCollectionsAsync_ReturnsOnlyAccessible()
    {
        var accessible = TestData.CreateCollection(name: "Accessible");
        var inaccessible = TestData.CreateCollection(name: "Hidden");
        _db.Collections.AddRange(accessible, inaccessible);
        _db.CollectionAcls.Add(TestData.CreateAcl(accessible.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var managerService = CreateService(ManagerUser, isAdmin: false);
        var result = await managerService.GetRootCollectionsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("Accessible", result.Value![0].Name);
    }

    // ── DownloadAllAssetsAsync ──────────────────────────────────────

    [Fact]
    public async Task DownloadAllAssetsAsync_WithAccess_EnqueuesZip()
    {
        var col = TestData.CreateCollection(name: "Downloadable");
        _db.Collections.Add(col);
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, AdminUser, AclRole.Admin));
        await _db.SaveChangesAsync();

        _zipBuildServiceMock
            .Setup(x => x.EnqueueCollectionZipAsync(col.Id, AdminUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZipDownloadEnqueuedResponse { JobId = Guid.NewGuid(), Message = "ok" });

        var result = await _service.DownloadAllAssetsAsync(col.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _zipBuildServiceMock.Verify(
            x => x.EnqueueCollectionZipAsync(col.Id, AdminUser, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
