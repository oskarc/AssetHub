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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Tests that AssetService correctly logs audit events during downloads.
/// </summary>
[Collection("Database")]
public class AssetServiceAuditTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _assetRepo = null!;
    private AssetCollectionRepository _acRepo = null!;
    private ICollectionRepository _colRepo = null!;
    private CollectionAuthorizationService _authService = null!;
    private Mock<IMinIOAdapter> _minioMock = null!;
    private Mock<IAuditService> _auditMock = null!;

    private const string BucketName = "test-bucket";
    private const string TestUser = "audit-test-user-001";

    public AssetServiceAuditTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();

        var cache = TestCacheHelper.CreateHybridCache();
        _assetRepo = new AssetRepository(_db, cache, NullLogger<AssetRepository>.Instance);
        _acRepo = new AssetCollectionRepository(_db, cache,
            NullLogger<AssetCollectionRepository>.Instance);
        _colRepo = new CollectionRepository(_db, cache, NullLogger<CollectionRepository>.Instance);

        _authService = new CollectionAuthorizationService(
            _db, _colRepo, CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance);

        _minioMock = new Mock<IMinIOAdapter>();
        _auditMock = new Mock<IAuditService>();

        _minioMock.Setup(m => m.GetPresignedDownloadUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://minio.test/presigned-url");
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private AssetQueryService CreateSut(CurrentUser currentUser)
    {
        var minioSettings = Options.Create(new MinIOSettings { BucketName = BucketName });

        return new AssetQueryService(
            new AssetQueryRepositories(_assetRepo, _acRepo, _colRepo),
            _authService,
            _minioMock.Object,
            _auditMock.Object,
            currentUser,
            minioSettings,
            NullLogger<AssetQueryService>.Instance);
    }

    // ── asset.downloaded audit event ─────────────────────────────────────────

    [Fact]
    public async Task GetRenditionUrl_ForceDownload_LogsAuditEvent()
    {
        // Arrange
        var col = TestData.CreateCollection(name: "AuditCol");
        var asset = TestData.CreateAsset(title: "Download Me");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, TestUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(TestUser, isSystemAdmin: false));

        // Act
        var result = await sut.GetRenditionUrlAsync(asset.Id, "original", forceDownload: true, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _auditMock.Verify(a => a.LogAsync(
            "asset.downloaded",
            "asset",
            asset.Id,
            TestUser,
            It.Is<Dictionary<string, object>>(d =>
                d["title"].ToString() == "Download Me" &&
                d["size"].ToString() == "original"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRenditionUrl_Preview_DoesNotLogAuditEvent()
    {
        // Arrange
        var col = TestData.CreateCollection(name: "PreviewCol");
        var asset = TestData.CreateAsset(title: "Preview Me");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, TestUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(TestUser, isSystemAdmin: false));

        // Act
        var result = await sut.GetRenditionUrlAsync(asset.Id, "original", forceDownload: false, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _auditMock.Verify(a => a.LogAsync(
            "asset.downloaded",
            It.IsAny<string>(),
            It.IsAny<Guid?>(),
            It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetRenditionUrl_ThumbDownload_LogsCorrectSize()
    {
        // Arrange
        var col = TestData.CreateCollection(name: "ThumbCol");
        var asset = TestData.CreateAsset(title: "Thumb Asset");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.CollectionAcls.Add(TestData.CreateAcl(col.Id, TestUser, AclRole.Viewer));
        await _db.SaveChangesAsync();

        var sut = CreateSut(new CurrentUser(TestUser, isSystemAdmin: false));

        // Act
        var result = await sut.GetRenditionUrlAsync(asset.Id, "thumb", forceDownload: true, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        _auditMock.Verify(a => a.LogAsync(
            "asset.downloaded",
            "asset",
            asset.Id,
            TestUser,
            It.Is<Dictionary<string, object>>(d => d["size"].ToString() == "thumb"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
