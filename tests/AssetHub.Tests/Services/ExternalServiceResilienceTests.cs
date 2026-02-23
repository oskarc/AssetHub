using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net.Sockets;

namespace AssetHub.Tests.Services;

/// <summary>
/// Tests for service behavior when external dependencies fail.
/// Verifies graceful degradation and appropriate error responses.
/// </summary>
[Collection("Database")]
public class ExternalServiceResilienceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _assetRepo = null!;
    private AssetCollectionRepository _acRepo = null!;
    private ICollectionRepository _colRepo = null!;
    private ShareRepository _shareRepo = null!;
    private CollectionAuthorizationService _authService = null!;
    private AssetDeletionService _deletionService = null!;
    private Mock<IMinIOAdapter> _minioMock = null!;
    private Mock<IAuditService> _auditMock = null!;
    private Mock<IMediaProcessingService> _mediaMock = null!;
    private Mock<IMalwareScannerService> _malwareMock = null!;

    private const string BucketName = "test-bucket";
    private const string TestUser = "resilience-test-user";

    public ExternalServiceResilienceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        _assetRepo = new AssetRepository(_db, cache, NullLogger<AssetRepository>.Instance);
        _acRepo = new AssetCollectionRepository(_db, cache,
            NullLogger<AssetCollectionRepository>.Instance);
        _colRepo = new CollectionRepository(_db, NullLogger<CollectionRepository>.Instance);
        _shareRepo = new ShareRepository(_db, NullLogger<ShareRepository>.Instance);

        _authService = new CollectionAuthorizationService(
            _db, NullLogger<CollectionAuthorizationService>.Instance);

        _minioMock = new Mock<IMinIOAdapter>();
        _auditMock = new Mock<IAuditService>();
        _mediaMock = new Mock<IMediaProcessingService>();
        _malwareMock = new Mock<IMalwareScannerService>();

        _deletionService = new AssetDeletionService(
            _assetRepo, _acRepo, _shareRepo, _minioMock.Object);

        // Default: malware scanner returns clean
        _malwareMock.Setup(m => m.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MalwareScanResult.Clean());
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private AssetService CreateSut(CurrentUser currentUser)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinIO:BucketName"] = BucketName
            })
            .Build();

        return new AssetService(
            _assetRepo,
            _acRepo,
            _colRepo,
            _authService,
            _minioMock.Object,
            _mediaMock.Object,
            _deletionService,
            _auditMock.Object,
            _malwareMock.Object,
            config,
            currentUser,
            new Mock<IHttpContextAccessor>().Object,
            NullLogger<AssetService>.Instance);
    }

    private async Task<Guid> CreateTestCollectionAsync()
    {
        var collection = new Collection
        {
            Name = "ResilienceTest",
            CreatedByUserId = TestUser,
            CreatedAt = DateTime.UtcNow
        };
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        _db.CollectionAcls.Add(new CollectionAcl
        {
            CollectionId = collection.Id,
            PrincipalType = PrincipalType.User,
            PrincipalId = TestUser,
            Role = AclRole.Contributor,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return collection.Id;
    }

    // ── MinIO Upload Failure Tests ───────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WhenMinIOThrowsStorageException_ReturnsServerError()
    {
        // Arrange
        var collectionId = await CreateTestCollectionAsync();
        var currentUser = new CurrentUser(TestUser, false);
        var sut = CreateSut(currentUser);

        _minioMock.Setup(m => m.UploadAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StorageException("Storage service is temporarily unavailable. Please try again."));

        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // JPEG header
        var contentType = "image/jpeg";

        // Act
        var result = await sut.UploadAsync(stream, "test.jpg", contentType, 100, collectionId, "Test", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.Error!.StatusCode);
        Assert.Contains("unavailable", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_WhenMinIOThrowsStorageException_DoesNotCreateAsset()
    {
        // Arrange
        var collectionId = await CreateTestCollectionAsync();
        var currentUser = new CurrentUser(TestUser, false);
        var sut = CreateSut(currentUser);

        _minioMock.Setup(m => m.UploadAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StorageException("Storage error"));

        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        // Act
        await sut.UploadAsync(stream, "test.jpg", "image/jpeg", 100, collectionId, "Test", CancellationToken.None);

        // Assert - no asset should be in the database
        var assetCount = await _assetRepo.CountByStatusAsync("processing", CancellationToken.None);
        Assert.Equal(0, assetCount);
    }

    // ── MinIO Presigned URL Failure Tests ────────────────────────────────────

    [Fact]
    public async Task InitUploadAsync_WhenMinIOThrowsStorageException_ReturnsServerError()
    {
        // Arrange
        var collectionId = await CreateTestCollectionAsync();
        var currentUser = new CurrentUser(TestUser, false);
        var sut = CreateSut(currentUser);

        _minioMock.Setup(m => m.GetPresignedUploadUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StorageException("Storage service failed to generate upload URL. Please try again."));

        var request = new InitUploadRequest
        {
            FileName = "test.jpg",
            ContentType = "image/jpeg",
            FileSize = 100,
            CollectionId = collectionId,
            Title = "Test"
        };

        // Act
        var result = await sut.InitUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.Error!.StatusCode);
        Assert.Contains("upload URL", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Malware Scanner Timeout Tests ────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_WhenMalwareScannerTimesOut_ReturnsServerError()
    {
        // Arrange
        var collectionId = await CreateTestCollectionAsync();
        var currentUser = new CurrentUser(TestUser, false);
        var sut = CreateSut(currentUser);

        _malwareMock.Setup(m => m.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MalwareScanResult.Failed("Connection timed out"));

        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        // Act
        var result = await sut.UploadAsync(stream, "test.jpg", "image/jpeg", 100, collectionId, "Test", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.Error!.StatusCode);
        Assert.Contains("scanning failed", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_WhenMalwareScannerUnavailable_DoesNotUploadToMinIO()
    {
        // Arrange
        var collectionId = await CreateTestCollectionAsync();
        var currentUser = new CurrentUser(TestUser, false);
        var sut = CreateSut(currentUser);

        _malwareMock.Setup(m => m.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MalwareScanResult.Failed("Connection refused"));

        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        // Act
        await sut.UploadAsync(stream, "test.jpg", "image/jpeg", 100, collectionId, "Test", CancellationToken.None);

        // Assert - MinIO upload should NOT have been called
        _minioMock.Verify(m => m.UploadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Download URL Failure Tests ───────────────────────────────────────────

    [Fact]
    public async Task GetRenditionUrlAsync_WhenMinIOThrowsStorageException_ReturnsServerError()
    {
        // Arrange
        var collectionId = await CreateTestCollectionAsync();
        var currentUser = new CurrentUser(TestUser, false);
        var sut = CreateSut(currentUser);

        // Create an asset first
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            Title = "Test Asset",
            OriginalObjectKey = "originals/test.jpg",
            ContentType = "image/jpeg",
            AssetType = AssetType.Image,
            SizeBytes = 100,
            Status = AssetStatus.Ready,
            CreatedByUserId = TestUser,
            CreatedAt = DateTime.UtcNow
        };
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(new AssetCollection
        {
            AssetId = asset.Id,
            CollectionId = collectionId,
            AddedByUserId = TestUser,
            AddedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _minioMock.Setup(m => m.GetPresignedDownloadUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StorageException("Storage service is temporarily unavailable. Please try again."));

        // Act
        var result = await sut.GetRenditionUrlAsync(asset.Id, "original", false, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.Error!.StatusCode);
        Assert.Contains("unavailable", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
