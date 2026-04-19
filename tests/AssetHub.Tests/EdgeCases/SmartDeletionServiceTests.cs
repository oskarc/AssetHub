using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.EdgeCases;

/// <summary>
/// Service-level smart deletion tests per V2 plan §10.6.
/// These exercise AssetService.DeleteAsync with real DB + real authorization,
/// verifying the multi-collection + permission-aware deletion logic.
/// </summary>
[Collection("Database")]
public class SmartDeletionServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _assetRepo = null!;
    private AssetCollectionRepository _acRepo = null!;
    private ICollectionRepository _colRepo = null!;
    private ShareRepository _shareRepo = null!;
    private AssetVersionRepository _versionRepo = null!;
    private CollectionAuthorizationService _authService = null!;
    private AssetDeletionService _deletionService = null!;
    private Mock<IMinIOAdapter> _minioMock = null!;
    private Mock<IAuditService> _auditMock = null!;

    private const string BucketName = "test-bucket";

    // User IDs used across tests
    private const string ManagerUser = "manager-user-001";
    private const string PartialUser = "partial-user-002";
    private const string AdminUser = "admin-user-003";

    public SmartDeletionServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();

        var cache = TestCacheHelper.CreateHybridCache();
        _assetRepo = new AssetRepository(_db, cache, NullLogger<AssetRepository>.Instance);
        _acRepo = new AssetCollectionRepository(_db, cache,
            NullLogger<AssetCollectionRepository>.Instance);
        _colRepo = new CollectionRepository(_db, cache, NullLogger<CollectionRepository>.Instance);
        _shareRepo = new ShareRepository(_db, NullLogger<ShareRepository>.Instance);

        _authService = new CollectionAuthorizationService(
            _db, CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance);

        _minioMock = new Mock<IMinIOAdapter>();
        _auditMock = new Mock<IAuditService>();
        _versionRepo = new AssetVersionRepository(_db, NullLogger<AssetVersionRepository>.Instance);

        _deletionService = new AssetDeletionService(
            _assetRepo, _acRepo, _versionRepo, _shareRepo, _minioMock.Object);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private AssetService CreateSut(CurrentUser currentUser)
    {
        var minioSettings = Options.Create(new MinIOSettings { BucketName = BucketName });

        return new AssetService(
            new AssetServiceRepositories(_assetRepo, _acRepo, _colRepo),
            _authService,
            _deletionService,
            _auditMock.Object,
            TestCacheHelper.CreateHybridCache(),
            currentUser,
            minioSettings);
    }

    private AssetQueryService CreateQuerySut(CurrentUser currentUser)
    {
        var minioSettings = Options.Create(new MinIOSettings { BucketName = BucketName });
        var repos = new AssetQueryRepositories(_assetRepo, _acRepo, _colRepo);

        return new AssetQueryService(
            repos,
            _authService,
            _minioMock.Object,
            _auditMock.Object,
            currentUser,
            minioSettings,
            NullLogger<AssetQueryService>.Instance);
    }

    // ── §10.6 Edge Case 1: asset in 1 collection → deleted permanently ──

    [Fact]
    public async Task Delete_AssetInOneCollection_ManagerDeletesPermanently()
    {
        // Arrange: 1 collection, 1 asset, user is Manager
        var col = TestData.CreateCollection(name: "OnlyCol");
        var asset = TestData.CreateAsset(title: "Sole Asset");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(ManagerUser, isSystemAdmin: false));

        // Act: delete without fromCollectionId (permanent mode)
        var result = await sut.DeleteAsync(asset.Id, fromCollectionId: null, CancellationToken.None);

        // Assert: success, asset permanently gone
        Assert.True(result.IsSuccess);
        var found = await _assetRepo.GetByIdAsync(asset.Id);
        Assert.Null(found);
    }

    // ── §10.6 Edge Case 2: asset in 2 collections, user has access to both ──

    [Fact]
    public async Task Delete_AssetIn2Collections_ManagerOnBoth_PermanentlyDeletes()
    {
        // Arrange: asset in col1 + col2, user is Manager on both
        var col1 = TestData.CreateCollection(name: "ColA");
        var col2 = TestData.CreateCollection(name: "ColB");
        var asset = TestData.CreateAsset(title: "Multi-Col Asset");
        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, ManagerUser, AclRole.Manager));
        _db.CollectionAcls.Add(TestData.CreateAcl(col2.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(ManagerUser, isSystemAdmin: false));

        // Act: permanent delete (user manages all collections)
        var result = await sut.DeleteAsync(asset.Id, fromCollectionId: null, CancellationToken.None);

        // Assert: asset permanently deleted
        Assert.True(result.IsSuccess);
        Assert.Null(await _assetRepo.GetByIdAsync(asset.Id));
    }

    [Fact]
    public async Task RemoveFromCollection_AssetIn2Collections_ManagerOnBoth_RemovesFromOne()
    {
        // Arrange: asset in col1 + col2, user is Manager on both
        var col1 = TestData.CreateCollection(name: "ColA");
        var col2 = TestData.CreateCollection(name: "ColB");
        var asset = TestData.CreateAsset(title: "Multi-Col Asset");
        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, ManagerUser, AclRole.Manager));
        _db.CollectionAcls.Add(TestData.CreateAcl(col2.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(ManagerUser, isSystemAdmin: false));

        // Act: remove from col1 only (fromCollectionId specified)
        var result = await sut.DeleteAsync(asset.Id, fromCollectionId: col1.Id, CancellationToken.None);

        // Assert: success, asset still exists, only col2 link remains
        Assert.True(result.IsSuccess);
        var found = await _assetRepo.GetByIdAsync(asset.Id);
        Assert.NotNull(found);
        var remaining = await _acRepo.GetCollectionIdsForAssetAsync(asset.Id);
        Assert.Single(remaining);
        Assert.Equal(col2.Id, remaining[0]);
    }

    // ── §10.6 Edge Case 3: asset in 2 collections, user lacks access to one ──

    [Fact]
    public async Task Delete_AssetIn2Collections_UserManagesOne_RemovesFromAuthorizedOnly()
    {
        // Arrange: asset in col1 (Manager) + col2 (no access)
        var col1 = TestData.CreateCollection(name: "Authorized");
        var col2 = TestData.CreateCollection(name: "Unauthorized");
        var asset = TestData.CreateAsset(title: "Partial-Access Asset");
        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        // PartialUser is Manager only on col1
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, PartialUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(PartialUser, isSystemAdmin: false));

        // Act: user tries permanent delete
        var result = await sut.DeleteAsync(asset.Id, fromCollectionId: null, CancellationToken.None);

        // Assert: success (from user's perspective), but asset survives
        Assert.True(result.IsSuccess);

        // Asset still exists in DB
        var found = await _assetRepo.GetByIdAsync(asset.Id);
        Assert.NotNull(found);

        // Link to col1 is removed, link to col2 is preserved
        var remaining = await _acRepo.GetCollectionIdsForAssetAsync(asset.Id);
        Assert.Single(remaining);
        Assert.Equal(col2.Id, remaining[0]);
    }

    // ── §10.6 Edge Case 4: verify asset still accessible from unauthorized collection ──

    [Fact]
    public async Task Delete_PartialAccess_AssetStillAccessibleFromUnauthorizedCollection()
    {
        // Arrange: asset in col1 (PartialUser=Manager) + col2 (no access for PartialUser)
        // Another user (ManagerUser) has Manager on col2
        var col1 = TestData.CreateCollection(name: "UserCol");
        var col2 = TestData.CreateCollection(name: "OtherCol");
        var asset = TestData.CreateAsset(title: "Surviving Asset");
        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, PartialUser, AclRole.Manager));
        _db.CollectionAcls.Add(TestData.CreateAcl(col2.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var sutPartial = CreateSut(new CurrentUser(PartialUser, isSystemAdmin: false));

        // Act: PartialUser "deletes" the asset
        var deleteResult = await sutPartial.DeleteAsync(asset.Id, fromCollectionId: null, CancellationToken.None);
        Assert.True(deleteResult.IsSuccess);

        // Now verify: ManagerUser with access to col2 can still retrieve the asset
        // Re-create a fresh DbContext to avoid stale tracking
        var dbName = new Npgsql.NpgsqlConnectionStringBuilder(_db.Database.GetConnectionString()!).Database!;
        var db2 = _fixture.CreateDbContextForExistingDb(dbName);
        var cache2 = TestCacheHelper.CreateHybridCache();
        var assetRepo2 = new AssetRepository(db2, cache2, NullLogger<AssetRepository>.Instance);
        var acRepo2 = new AssetCollectionRepository(db2, cache2,
            NullLogger<AssetCollectionRepository>.Instance);
        var authService2 = new CollectionAuthorizationService(db2,
            CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance);

        // Asset still exists
        var found = await assetRepo2.GetByIdAsync(asset.Id);
        Assert.NotNull(found);

        // ManagerUser still has access via col2
        var hasAccess = await authService2.CheckAccessAsync(
            ManagerUser, col2.Id, RoleHierarchy.Roles.Viewer, CancellationToken.None);
        Assert.True(hasAccess);

        // Asset is still linked to col2
        var linkedCollections = await acRepo2.GetCollectionIdsForAssetAsync(asset.Id);
        Assert.Single(linkedCollections);
        Assert.Equal(col2.Id, linkedCollections[0]);

        await db2.DisposeAsync();
    }

    // ── GetDeletionContext ──────────────────────────────────────────

    [Fact]
    public async Task GetDeletionContext_SingleCollection_CanDeletePermanently()
    {
        var col = TestData.CreateCollection(name: "solo");
        var asset = TestData.CreateAsset(title: "ctx-1");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, ManagerUser, AclRole.Manager));
        await _db.SaveChangesAsync();

        var sut = CreateQuerySut(new CurrentUser(ManagerUser, isSystemAdmin: false));
        var result = await sut.GetDeletionContextAsync(asset.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CollectionCount);
        Assert.True(result.Value.CanDeletePermanently);
    }

    [Fact]
    public async Task GetDeletionContext_MultipleCollections_PartialAccess_CannotDeletePermanently()
    {
        var col1 = TestData.CreateCollection(name: "ctx-a");
        var col2 = TestData.CreateCollection(name: "ctx-b");
        var asset = TestData.CreateAsset(title: "ctx-2");
        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, PartialUser, AclRole.Manager));
        // No access to col2
        await _db.SaveChangesAsync();

        var sut = CreateQuerySut(new CurrentUser(PartialUser, isSystemAdmin: false));
        var result = await sut.GetDeletionContextAsync(asset.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.CollectionCount);
        Assert.False(result.Value.CanDeletePermanently);
    }

    // ── System admin bypass ─────────────────────────────────────────

    [Fact]
    public async Task Delete_SystemAdmin_AlwaysDeletesPermanently()
    {
        var col1 = TestData.CreateCollection(name: "AdminCol1");
        var col2 = TestData.CreateCollection(name: "AdminCol2");
        var asset = TestData.CreateAsset(title: "Admin Delete");
        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        // No ACLs for admin — system admin bypasses
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(AdminUser, isSystemAdmin: true));

        var result = await sut.DeleteAsync(asset.Id, fromCollectionId: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await _assetRepo.GetByIdAsync(asset.Id));
    }

    // ── No access at all ────────────────────────────────────────────

    [Fact]
    public async Task Delete_NoAccessToAnyCollection_ReturnsForbidden()
    {
        var col = TestData.CreateCollection(name: "Locked");
        var asset = TestData.CreateAsset(title: "No-Access");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        // No ACL for PartialUser
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(PartialUser, isSystemAdmin: false));

        var result = await sut.DeleteAsync(asset.Id, fromCollectionId: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.Forbidden().Message, result.Error!.Message);

        // Asset untouched
        Assert.NotNull(await _assetRepo.GetByIdAsync(asset.Id));
    }
}
