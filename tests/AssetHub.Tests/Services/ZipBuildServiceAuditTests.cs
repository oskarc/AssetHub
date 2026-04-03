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
using Wolverine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Tests that ZipBuildService logs audit events on successful ZIP completion.
/// </summary>
[Collection("Database")]
public class ZipBuildServiceAuditTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private string _dbName = null!;
    private AssetRepository _assetRepo = null!;
    private CollectionRepository _colRepo = null!;
    private Mock<IMinIOAdapter> _minioMock = null!;
    private Mock<IAuditService> _auditMock = null!;
    private Mock<IMessageBus> _messageBusMock = null!;

    private const string BucketName = "test-bucket";
    private const string TestUser = "zip-audit-user-001";

    public ZipBuildServiceAuditTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _dbName = $"test_{Guid.NewGuid():N}";
        _db = await _fixture.CreateDbContextAsync(_dbName);

        var cache = TestCacheHelper.CreateHybridCache();
        _assetRepo = new AssetRepository(_db, cache, NullLogger<AssetRepository>.Instance);
        _colRepo = new CollectionRepository(_db, cache, NullLogger<CollectionRepository>.Instance);

        _minioMock = new Mock<IMinIOAdapter>();
        _auditMock = new Mock<IAuditService>();
        _messageBusMock = new Mock<IMessageBus>();

        // Mock MinIO download to return a small stream
        _minioMock.Setup(m => m.DownloadAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 }));

        // Mock MinIO upload to succeed
        _minioMock.Setup(m => m.UploadAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private ZipBuildService CreateSut()
    {
        var minioSettings = Options.Create(new MinIOSettings { BucketName = BucketName });

        // Mock DbContextFactory to return a fresh context pointing at the same test DB
        var dbFactoryMock = new Mock<IDbContextFactory<AssetHubDbContext>>();
        dbFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _fixture.CreateDbContextForExistingDb(_dbName));

        return new ZipBuildService(
            new ZipBuildDataDependencies(dbFactoryMock.Object, _assetRepo, _colRepo),
            _minioMock.Object,
            _messageBusMock.Object,
            _auditMock.Object,
            minioSettings,
            NullLogger<ZipBuildService>.Instance);
    }

    // ── collection.downloaded audit event ────────────────────────────────────

    [Fact]
    public async Task BuildZip_Success_LogsCollectionDownloadedAuditEvent()
    {
        // Arrange: collection with 2 assets
        var col = TestData.CreateCollection(name: "ZipAuditCol");
        var asset1 = TestData.CreateAsset(title: "Asset One");
        var asset2 = TestData.CreateAsset(title: "Asset Two", sizeBytes: 2048);
        _db.Collections.Add(col);
        _db.Assets.AddRange(asset1, asset2);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset1.Id, col.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset2.Id, col.Id));

        var zipDownload = new ZipDownload
        {
            Id = Guid.NewGuid(),
            Status = ZipDownloadStatus.Pending,
            ScopeType = ShareScopeType.Collection,
            ScopeId = col.Id,
            ZipFileName = "ZipAuditCol_assets.zip",
            RequestedByUserId = TestUser,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _db.ZipDownloads.Add(zipDownload);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        await sut.BuildZipAsync(zipDownload.Id, CancellationToken.None);

        // Assert: audit event logged with correct details
        _auditMock.Verify(a => a.LogAsync(
            "collection.downloaded",
            "collection",
            col.Id,
            TestUser,
            It.Is<Dictionary<string, object>>(d =>
                (int)d["assetCount"] == 2 &&
                d.ContainsKey("sizeBytes")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildZip_NoAssets_DoesNotLogAuditEvent()
    {
        // Arrange: collection with no assets
        var col = TestData.CreateCollection(name: "EmptyCol");
        _db.Collections.Add(col);

        var zipDownload = new ZipDownload
        {
            Id = Guid.NewGuid(),
            Status = ZipDownloadStatus.Pending,
            ScopeType = ShareScopeType.Collection,
            ScopeId = col.Id,
            ZipFileName = "EmptyCol_assets.zip",
            RequestedByUserId = TestUser,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _db.ZipDownloads.Add(zipDownload);
        await _db.SaveChangesAsync();

        var sut = CreateSut();

        // Act
        await sut.BuildZipAsync(zipDownload.Id, CancellationToken.None);

        // Assert: no audit event since build failed (no assets)
        _auditMock.Verify(a => a.LogAsync(
            "collection.downloaded",
            It.IsAny<string>(),
            It.IsAny<Guid?>(),
            It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
