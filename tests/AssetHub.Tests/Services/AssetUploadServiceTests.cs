using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Comprehensive tests for AssetUploadService covering:
/// - Content-type rejection (disallowed types)
/// - Magic byte validation (content-type spoofing prevention)
/// - File size limits
/// - Authorization checks (Contributor role required)
/// - Presigned upload flow (InitUpload + ConfirmUpload)
/// </summary>
[Collection("Database")]
public class AssetUploadServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _assetRepo = null!;
    private AssetCollectionRepository _acRepo = null!;
    private CollectionAuthorizationService _authService = null!;
    private Mock<IMinIOAdapter> _minioMock = null!;
    private Mock<IAuditService> _auditMock = null!;
    private Mock<IMediaProcessingService> _mediaMock = null!;
    private Mock<IMalwareScannerService> _malwareMock = null!;

    private const string BucketName = "test-bucket";
    private const string ContributorUser = "upload-test-contributor-001";
    private const string ViewerUser = "upload-test-viewer-001";
    private const string NonMemberUser = "upload-test-non-member-001";
    private const int MaxUploadSizeMb = 100;

    public AssetUploadServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        _assetRepo = new AssetRepository(_db, cache, NullLogger<AssetRepository>.Instance);
        _acRepo = new AssetCollectionRepository(_db, cache, NullLogger<AssetCollectionRepository>.Instance);
        _authService = new CollectionAuthorizationService(_db, NullLogger<CollectionAuthorizationService>.Instance);

        _minioMock = new Mock<IMinIOAdapter>();
        _auditMock = new Mock<IAuditService>();
        _mediaMock = new Mock<IMediaProcessingService>();
        _malwareMock = new Mock<IMalwareScannerService>();

        // Default mock setups for clean upload scenario
        _minioMock.Setup(m => m.UploadAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _minioMock.Setup(m => m.GetPresignedUploadUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://minio.local/presigned-upload-url");

        _mediaMock.Setup(m => m.ScheduleProcessingAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("job-123");

        _malwareMock.Setup(m => m.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MalwareScanResult.Clean());
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private AssetUploadService CreateSut(string userId, bool isSystemAdmin = false)
    {
        var minioSettings = Options.Create(new MinIOSettings { BucketName = BucketName });
        var appSettings = Options.Create(new AppSettings { MaxUploadSizeMb = MaxUploadSizeMb });

        return new AssetUploadService(
            _assetRepo,
            _acRepo,
            _authService,
            _minioMock.Object,
            _mediaMock.Object,
            _malwareMock.Object,
            _auditMock.Object,
            new CurrentUser(userId, isSystemAdmin),
            minioSettings,
            appSettings,
            NullLogger<AssetUploadService>.Instance);
    }

    private async Task<Guid> CreateCollectionWithAccessAsync()
    {
        var collection = new Collection
        {
            Name = "UploadTestCollection",
            CreatedByUserId = ContributorUser,
            CreatedAt = DateTime.UtcNow
        };
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        // Grant contributor access
        _db.CollectionAcls.Add(new CollectionAcl
        {
            CollectionId = collection.Id,
            PrincipalType = PrincipalType.User,
            PrincipalId = ContributorUser,
            Role = AclRole.Contributor,
            CreatedAt = DateTime.UtcNow
        });

        // Grant viewer access (cannot upload)
        _db.CollectionAcls.Add(new CollectionAcl
        {
            CollectionId = collection.Id,
            PrincipalType = PrincipalType.User,
            PrincipalId = ViewerUser,
            Role = AclRole.Viewer,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return collection.Id;
    }

    // ── Content-Type Rejection Tests ─────────────────────────────────────────

    [Theory]
    [InlineData("application/x-executable")]
    [InlineData("application/x-msdownload")]
    [InlineData("application/x-sh")]
    [InlineData("text/x-script.python")]
    public async Task UploadAsync_DisallowedContentType_ReturnsBadRequest(string contentType)
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        using var stream = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0x03 });

        var result = await sut.UploadAsync(stream, "malicious.exe", contentType, 100, collectionId, "Evil File", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("not allowed", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("video/mp4")]
    [InlineData("audio/mpeg")]
    [InlineData("application/pdf")]
    public async Task UploadAsync_AllowedContentType_Succeeds(string contentType)
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // Use valid magic bytes for each type
        var magicBytes = GetMagicBytesForContentType(contentType);
        using var stream = new MemoryStream(magicBytes);

        var result = await sut.UploadAsync(stream, "valid-file", contentType, magicBytes.Length, collectionId, "Valid File", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("processing", result.Value!.Status);
    }

    // ── Magic Byte Validation Tests (Content-Type Spoofing Prevention) ──────

    [Fact]
    public async Task UploadAsync_ContentTypeSpoofing_JpegHeaderWithPngType_ReturnsBadRequest()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // JPEG magic bytes but claiming PNG content type
        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var stream = new MemoryStream(jpegHeader);

        var result = await sut.UploadAsync(stream, "fake.png", "image/png", jpegHeader.Length, collectionId, "Spoofed PNG", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_ContentTypeSpoofing_GarbageBytesWithJpegType_ReturnsBadRequest()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // Random bytes claiming to be JPEG
        var garbage = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };
        using var stream = new MemoryStream(garbage);

        var result = await sut.UploadAsync(stream, "fake.jpg", "image/jpeg", garbage.Length, collectionId, "Not a JPEG", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_ValidMagicBytes_Succeeds()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // Valid JPEG header
        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var stream = new MemoryStream(jpegHeader);

        var result = await sut.UploadAsync(stream, "valid.jpg", "image/jpeg", jpegHeader.Length, collectionId, "Valid JPEG", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ── File Size Limit Tests ────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_FileSizeExceedsLimit_ReturnsBadRequest()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // 100MB limit, try 150MB
        var oversizedFileSize = (long)(MaxUploadSizeMb + 50) * 1024 * 1024;
        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        var result = await sut.UploadAsync(stream, "huge.jpg", "image/jpeg", oversizedFileSize, collectionId, "Too Big", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("exceeds", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(MaxUploadSizeMb.ToString(), result.Error.Message);
    }

    [Fact]
    public async Task UploadAsync_FileSizeAtLimit_Succeeds()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // Exactly at the limit
        var atLimitFileSize = (long)MaxUploadSizeMb * 1024 * 1024;
        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var stream = new MemoryStream(jpegHeader);

        var result = await sut.UploadAsync(stream, "max.jpg", "image/jpeg", atLimitFileSize, collectionId, "At Limit", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UploadAsync_EmptyFile_ReturnsBadRequest()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        using var stream = new MemoryStream();

        var result = await sut.UploadAsync(stream, "empty.jpg", "image/jpeg", 0, collectionId, "Empty", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("required", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Authorization Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_ViewerRole_ReturnsForbidden()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ViewerUser); // Viewer, not Contributor

        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var stream = new MemoryStream(jpegHeader);

        var result = await sut.UploadAsync(stream, "test.jpg", "image/jpeg", jpegHeader.Length, collectionId, "Test", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UploadAsync_NonMember_ReturnsForbidden()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(NonMemberUser); // No ACL entry at all

        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var stream = new MemoryStream(jpegHeader);

        var result = await sut.UploadAsync(stream, "test.jpg", "image/jpeg", jpegHeader.Length, collectionId, "Test", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UploadAsync_ContributorRole_Succeeds()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var stream = new MemoryStream(jpegHeader);

        var result = await sut.UploadAsync(stream, "test.jpg", "image/jpeg", jpegHeader.Length, collectionId, "Test", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ── Presigned Upload Flow (InitUpload + ConfirmUpload) ───────────────────

    [Fact]
    public async Task InitUploadAsync_ContributorRole_ReturnsPresignedUrl()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        var request = new InitUploadRequest
        {
            FileName = "large-video.mp4",
            ContentType = "video/mp4",
            FileSize = 50 * 1024 * 1024, // 50MB
            Title = "Large Video",
            CollectionId = collectionId
        };

        var result = await sut.InitUploadAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value.AssetId);
        Assert.Contains("presigned", result.Value.UploadUrl, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Value.ExpiresInSeconds > 0);
    }

    [Fact]
    public async Task InitUploadAsync_ViewerRole_ReturnsForbidden()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ViewerUser);

        var request = new InitUploadRequest
        {
            FileName = "test.mp4",
            ContentType = "video/mp4",
            FileSize = 1024,
            CollectionId = collectionId
        };

        var result = await sut.InitUploadAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task InitUploadAsync_DisallowedContentType_ReturnsBadRequest()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        var request = new InitUploadRequest
        {
            FileName = "script.sh",
            ContentType = "application/x-sh",
            FileSize = 1024,
            CollectionId = collectionId
        };

        var result = await sut.InitUploadAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("not allowed", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitUploadAsync_ExceedsFileSize_ReturnsBadRequest()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        var request = new InitUploadRequest
        {
            FileName = "huge.mp4",
            ContentType = "video/mp4",
            FileSize = (long)(MaxUploadSizeMb + 100) * 1024 * 1024,
            CollectionId = collectionId
        };

        var result = await sut.InitUploadAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("exceeds", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitUploadAsync_NoCollection_NonAdmin_ReturnsForbidden()
    {
        var sut = CreateSut(ContributorUser, isSystemAdmin: false);

        var request = new InitUploadRequest
        {
            FileName = "standalone.mp4",
            ContentType = "video/mp4",
            FileSize = 1024,
            CollectionId = null // No collection - requires admin
        };

        var result = await sut.InitUploadAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task InitUploadAsync_NoCollection_SystemAdmin_Succeeds()
    {
        var sut = CreateSut("system-admin-user", isSystemAdmin: true);

        var request = new InitUploadRequest
        {
            FileName = "standalone.mp4",
            ContentType = "video/mp4",
            FileSize = 1024,
            CollectionId = null
        };

        var result = await sut.InitUploadAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task ConfirmUploadAsync_ValidUpload_TransitionsToProcessing()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // Step 1: Init upload
        var initRequest = new InitUploadRequest
        {
            FileName = "video.mp4",
            ContentType = "video/mp4",
            FileSize = 5 * 1024 * 1024,
            Title = "Test Video",
            CollectionId = collectionId
        };
        var initResult = await sut.InitUploadAsync(initRequest, CancellationToken.None);
        Assert.True(initResult.IsSuccess);
        var assetId = initResult.Value!.AssetId;

        // Mock that file exists in storage after "upload"
        _minioMock.Setup(m => m.StatObjectAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(5 * 1024 * 1024, "video/mp4", "etag123"));

        // Mock download for magic byte validation - valid MP4 header
        var mp4Header = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 };
        _minioMock.Setup(m => m.DownloadRangeAsync(BucketName, It.IsAny<string>(), 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mp4Header);

        // Mock full download for malware scan
        _minioMock.Setup(m => m.DownloadAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(mp4Header));

        // Step 2: Confirm upload
        var confirmResult = await sut.ConfirmUploadAsync(assetId, CancellationToken.None);

        Assert.True(confirmResult.IsSuccess);
        Assert.Equal("processing", confirmResult.Value!.Status);
        Assert.NotNull(confirmResult.Value.JobId);
    }

    [Fact]
    public async Task ConfirmUploadAsync_FileNotInStorage_ReturnsBadRequest()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // Init upload
        var initRequest = new InitUploadRequest
        {
            FileName = "missing.mp4",
            ContentType = "video/mp4",
            FileSize = 1024,
            CollectionId = collectionId
        };
        var initResult = await sut.InitUploadAsync(initRequest, CancellationToken.None);
        var assetId = initResult.Value!.AssetId;

        // File doesn't exist in storage
        _minioMock.Setup(m => m.StatObjectAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ObjectStatInfo?)null);

        var confirmResult = await sut.ConfirmUploadAsync(assetId, CancellationToken.None);

        Assert.False(confirmResult.IsSuccess);
        Assert.Equal(400, confirmResult.Error!.StatusCode);
        Assert.Contains("not found", confirmResult.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmUploadAsync_MagicBytesMismatch_DeletesFileAndReturnsError()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // Init upload as MP4
        var initRequest = new InitUploadRequest
        {
            FileName = "fake.mp4",
            ContentType = "video/mp4",
            FileSize = 1024,
            CollectionId = collectionId
        };
        var initResult = await sut.InitUploadAsync(initRequest, CancellationToken.None);
        var assetId = initResult.Value!.AssetId;

        // File exists but has wrong magic bytes (JPEG instead of MP4)
        _minioMock.Setup(m => m.StatObjectAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(1024, "video/mp4", "etag123"));

        var jpegHeader = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG, not MP4
        _minioMock.Setup(m => m.DownloadRangeAsync(BucketName, It.IsAny<string>(), 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jpegHeader);

        var confirmResult = await sut.ConfirmUploadAsync(assetId, CancellationToken.None);

        Assert.False(confirmResult.IsSuccess);
        Assert.Equal(400, confirmResult.Error!.StatusCode);
        Assert.Contains("does not match", confirmResult.Error.Message, StringComparison.OrdinalIgnoreCase);

        // Verify file was deleted from storage
        _minioMock.Verify(m => m.DeleteAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmUploadAsync_MalwareDetected_DeletesFileAndReturnsError()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var sut = CreateSut(ContributorUser);

        // Init upload
        var initRequest = new InitUploadRequest
        {
            FileName = "infected.mp4",
            ContentType = "video/mp4",
            FileSize = 1024,
            CollectionId = collectionId
        };
        var initResult = await sut.InitUploadAsync(initRequest, CancellationToken.None);
        var assetId = initResult.Value!.AssetId;

        // File exists with valid magic bytes
        _minioMock.Setup(m => m.StatObjectAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(1024, "video/mp4", "etag123"));

        var mp4Header = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 };
        _minioMock.Setup(m => m.DownloadRangeAsync(BucketName, It.IsAny<string>(), 0, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mp4Header);

        _minioMock.Setup(m => m.DownloadAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(mp4Header));

        // But malware scanner detects infection
        _malwareMock.Setup(m => m.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MalwareScanResult.Infected("Test.Malware.Eicar"));

        var confirmResult = await sut.ConfirmUploadAsync(assetId, CancellationToken.None);

        Assert.False(confirmResult.IsSuccess);
        Assert.Equal(400, confirmResult.Error!.StatusCode);
        Assert.Contains("malware", confirmResult.Error.Message, StringComparison.OrdinalIgnoreCase);

        // Verify file was deleted
        _minioMock.Verify(m => m.DeleteAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify audit event was logged
        _auditMock.Verify(a => a.LogAsync(
            "asset.malware_detected",
            It.IsAny<string>(),
            assetId,
            ContributorUser,
            It.Is<Dictionary<string, object>>(d => d.ContainsKey("threatName")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmUploadAsync_WrongUser_ReturnsForbidden()
    {
        var collectionId = await CreateCollectionWithAccessAsync();
        var contributorSut = CreateSut(ContributorUser);
        var viewerSut = CreateSut(ViewerUser);

        // Contributor initiates upload
        var initRequest = new InitUploadRequest
        {
            FileName = "test.mp4",
            ContentType = "video/mp4",
            FileSize = 1024,
            CollectionId = collectionId
        };
        var initResult = await contributorSut.InitUploadAsync(initRequest, CancellationToken.None);
        var assetId = initResult.Value!.AssetId;

        // Different user tries to confirm
        var confirmResult = await viewerSut.ConfirmUploadAsync(assetId, CancellationToken.None);

        Assert.False(confirmResult.IsSuccess);
        Assert.Equal(403, confirmResult.Error!.StatusCode);
    }

    [Fact]
    public async Task ConfirmUploadAsync_AssetNotFound_ReturnsNotFound()
    {
        var sut = CreateSut(ContributorUser);

        var result = await sut.ConfirmUploadAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    // ── Helper Methods ───────────────────────────────────────────────────────

    private static byte[] GetMagicBytesForContentType(string contentType) => contentType switch
    {
        "image/jpeg" => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
        "image/png" => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
        "video/mp4" => new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 },
        "audio/mpeg" => new byte[] { 0xFF, 0xFB, 0x90, 0x00 },
        "application/pdf" => new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, // %PDF-
        _ => new byte[] { 0x00, 0x01, 0x02, 0x03 }
    };
}
