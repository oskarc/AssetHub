using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit tests for ShareAccessService — tests token validation, password verification,
/// access token generation, and IDOR protection with mocked dependencies.
/// </summary>
public class ShareAccessServiceTests
{
    private readonly Mock<IShareRepository> _shareRepoMock;
    private readonly Mock<IAssetRepository> _assetRepoMock;
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepoMock;
    private readonly Mock<ICollectionRepository> _collectionRepoMock;
    private readonly Mock<ICollectionAuthorizationService> _authServiceMock;
    private readonly Mock<IShareService> _shareServiceMock;
    private readonly Mock<IMinIOAdapter> _minioAdapterMock;
    private readonly Mock<IZipBuildService> _zipBuildServiceMock;
    private readonly Mock<IAuditService> _auditMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IDataProtectionProvider> _dataProtectionMock;
    private readonly Mock<IDataProtector> _dataProtectorMock;
    private readonly IOptions<MinIOSettings> _minioSettings;

    private const string TestUserId = "test-user-001";
    private const string TestPassword = "secure-password-123";
    private const string WrongPassword = "wrong-password";

    public ShareAccessServiceTests()
    {
        _shareRepoMock = new Mock<IShareRepository>();
        _assetRepoMock = new Mock<IAssetRepository>();
        _assetCollectionRepoMock = new Mock<IAssetCollectionRepository>();
        _collectionRepoMock = new Mock<ICollectionRepository>();
        _authServiceMock = new Mock<ICollectionAuthorizationService>();
        _shareServiceMock = new Mock<IShareService>();
        _minioAdapterMock = new Mock<IMinIOAdapter>();
        _zipBuildServiceMock = new Mock<IZipBuildService>();
        _auditMock = new Mock<IAuditService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _dataProtectionMock = new Mock<IDataProtectionProvider>();
        _dataProtectorMock = new Mock<IDataProtector>();

        _dataProtectionMock
            .Setup(x => x.CreateProtector(It.IsAny<string>()))
            .Returns(_dataProtectorMock.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _minioSettings = Options.Create(new MinIOSettings { BucketName = "test-bucket" });
    }

    private PublicShareAccessService CreatePublicService()
    {
        return new PublicShareAccessService(
            _shareRepoMock.Object,
            _assetRepoMock.Object,
            _assetCollectionRepoMock.Object,
            _collectionRepoMock.Object,
            _zipBuildServiceMock.Object,
            _auditMock.Object,
            _minioAdapterMock.Object,
            _minioSettings,
            _dataProtectionMock.Object,
            _httpContextAccessorMock.Object,
            NullLogger<PublicShareAccessService>.Instance);
    }

    private AuthenticatedShareAccessService CreateAuthenticatedService(CurrentUser? currentUser = null)
    {
        return new AuthenticatedShareAccessService(
            _shareRepoMock.Object,
            _authServiceMock.Object,
            _shareServiceMock.Object,
            _auditMock.Object,
            _dataProtectionMock.Object,
            currentUser ?? CurrentUser.Anonymous,
            NullLogger<AuthenticatedShareAccessService>.Instance);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetSharedContentAsync — Token Lookup Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSharedContentAsync_InvalidToken_ReturnsNotFound()
    {
        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Share?)null);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync("invalid-token", null, 0, 50, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
        Assert.Contains("not found", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSharedContentAsync_ExpiredShare_ReturnsExpiredError()
    {
        var (share, token) = TestData.CreateShareWithToken(
            expiresAt: DateTime.UtcNow.AddDays(-1)); // Expired yesterday

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, null, 0, 50, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("expired", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSharedContentAsync_RevokedShare_ReturnsRevokedError()
    {
        var (share, token) = TestData.CreateShareWithToken(revoked: true);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, null, 0, 50, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("revoked", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetSharedContentAsync — Password Verification Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSharedContentAsync_PasswordRequired_NoPasswordProvided_Returns401()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword);
        var (share, token) = TestData.CreateShareWithToken(passwordHash: passwordHash);
        TestData.CreateAsset(id: share.ScopeId);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, null, 0, 50, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.Error!.StatusCode);
        Assert.Equal("PASSWORD_REQUIRED", result.Error.Code);
    }

    [Fact]
    public async Task GetSharedContentAsync_WrongPassword_Returns401()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword);
        var (share, token) = TestData.CreateShareWithToken(passwordHash: passwordHash);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, WrongPassword, 0, 50, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.Error!.StatusCode);
        Assert.Equal("UNAUTHORIZED", result.Error.Code);
    }

    [Fact]
    public async Task GetSharedContentAsync_WrongPassword_LogsFailedAttempt()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword);
        var (share, token) = TestData.CreateShareWithToken(passwordHash: passwordHash);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        await service.GetSharedContentAsync(token, WrongPassword, 0, 50, CancellationToken.None);

        // Verify audit log was called for failed password attempt
        _auditMock.Verify(a => a.LogAsync(
            "share.password_failed",
            "share",
            share.Id,
            It.IsAny<string?>(),
            It.Is<Dictionary<string, object>?>(d => d != null && d.ContainsKey("ip")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSharedContentAsync_CorrectPassword_ReturnsAsset()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword);
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(
            scopeId: asset.Id,
            passwordHash: passwordHash);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _assetRepoMock.Setup(x => x.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, TestPassword, 0, 50, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = Assert.IsType<SharedAssetDto>(result.Value);
        Assert.Equal(asset.Id, dto.Id);
        Assert.Equal(asset.Title, dto.Title);
    }

    [Fact]
    public async Task GetSharedContentAsync_NoPassword_PublicShare_ReturnsAsset()
    {
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(
            scopeId: asset.Id,
            passwordHash: null);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _assetRepoMock.Setup(x => x.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, null, 0, 50, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = Assert.IsType<SharedAssetDto>(result.Value);
        Assert.Equal(asset.Id, dto.Id);
    }

    [Fact]
    public async Task GetSharedContentAsync_IncrementsAccessCount()
    {
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(scopeId: asset.Id);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _assetRepoMock.Setup(x => x.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var service = CreatePublicService();
        await service.GetSharedContentAsync(token, null, 0, 50, CancellationToken.None);

        _shareRepoMock.Verify(x => x.IncrementAccessAsync(share.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetSharedContentAsync — Collection Share Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSharedContentAsync_CollectionShare_ReturnsCollectionWithAssets()
    {
        var collection = TestData.CreateCollection(name: "Shared Collection");
        var assets = new List<Asset>
        {
            TestData.CreateAsset(title: "Asset 1"),
            TestData.CreateAsset(title: "Asset 2")
        };
        var (share, token) = TestData.CreateShareWithToken(
            scopeType: ShareScopeType.Collection,
            scopeId: collection.Id);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _collectionRepoMock.Setup(x => x.GetByIdAsync(collection.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _assetRepoMock.Setup(x => x.CountByCollectionAsync(collection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _assetRepoMock.Setup(x => x.GetByCollectionAsync(collection.Id, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assets);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, null, 0, 50, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = Assert.IsType<SharedCollectionDto>(result.Value);
        Assert.Equal(collection.Id, dto.Id);
        Assert.Equal("Shared Collection", dto.Name);
        Assert.Equal(2, dto.TotalAssets);
        Assert.Equal(2, dto.Assets.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetDownloadUrlAsync — IDOR Protection Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDownloadUrlAsync_AssetShare_ReturnsPresignedUrl()
    {
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(scopeId: asset.Id);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _assetRepoMock.Setup(x => x.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);
        _minioAdapterMock.Setup(x => x.GetPresignedDownloadUrlAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), true, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://minio/presigned-url");

        var service = CreatePublicService();
        var result = await service.GetDownloadUrlAsync(token, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://minio/presigned-url", result.Value);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_CollectionShare_AssetNotInCollection_ReturnsForbidden()
    {
        var collection = TestData.CreateCollection();
        var unrelatedAsset = TestData.CreateAsset(title: "Unrelated Asset");
        var (share, token) = TestData.CreateShareWithToken(
            scopeType: ShareScopeType.Collection,
            scopeId: collection.Id);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _assetRepoMock.Setup(x => x.GetByIdAsync(unrelatedAsset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unrelatedAsset);
        _assetCollectionRepoMock.Setup(x => x.BelongsToCollectionAsync(unrelatedAsset.Id, collection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Asset does NOT belong to the shared collection

        var service = CreatePublicService();
        var result = await service.GetDownloadUrlAsync(token, null, unrelatedAsset.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
        Assert.Contains("not found in this shared collection", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_CollectionShare_AssetInCollection_ReturnsUrl()
    {
        var collection = TestData.CreateCollection();
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(
            scopeType: ShareScopeType.Collection,
            scopeId: collection.Id);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _assetRepoMock.Setup(x => x.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);
        _assetCollectionRepoMock.Setup(x => x.BelongsToCollectionAsync(asset.Id, collection.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Asset belongs to the shared collection
        _minioAdapterMock.Setup(x => x.GetPresignedDownloadUrlAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), true, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://minio/presigned-url");

        var service = CreatePublicService();
        var result = await service.GetDownloadUrlAsync(token, null, asset.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://minio/presigned-url", result.Value);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_CollectionShare_NoAssetIdProvided_ReturnsBadRequest()
    {
        var collection = TestData.CreateCollection();
        var (share, token) = TestData.CreateShareWithToken(
            scopeType: ShareScopeType.Collection,
            scopeId: collection.Id);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        var result = await service.GetDownloadUrlAsync(token, null, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("assetId", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_DownloadNotPermitted_ReturnsForbidden()
    {
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(scopeId: asset.Id);
        // Override permissions to deny download
        share.PermissionsJson = new Dictionary<string, bool> { ["download"] = false, ["preview"] = true };

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _assetRepoMock.Setup(x => x.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var service = CreatePublicService();
        var result = await service.GetDownloadUrlAsync(token, null, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
        Assert.Contains("Download permission", result.Error.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CreateAccessTokenAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateAccessTokenAsync_ValidPassword_ReturnsAccessToken()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword);
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(
            scopeId: asset.Id,
            passwordHash: passwordHash);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _dataProtectorMock.Setup(x => x.Protect(It.IsAny<byte[]>()))
            .Returns<byte[]>(input => input);

        var service = CreatePublicService();
        var result = await service.CreateAccessTokenAsync(token, TestPassword, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.ExpiresInSeconds > 0);
    }

    [Fact]
    public async Task CreateAccessTokenAsync_InvalidPassword_Returns401()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword);
        var (share, token) = TestData.CreateShareWithToken(passwordHash: passwordHash);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        var result = await service.CreateAccessTokenAsync(token, WrongPassword, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.Error!.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RevokeShareAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RevokeShareAsync_Owner_Success()
    {
        var share = TestData.CreateShare(createdByUserId: TestUserId);
        _shareRepoMock.Setup(x => x.GetByIdAsync(share.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var currentUser = new CurrentUser(TestUserId, isSystemAdmin: false);
        var service = CreateAuthenticatedService(currentUser);
        var result = await service.RevokeShareAsync(share.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _shareRepoMock.Verify(x => x.UpdateAsync(
            It.Is<Share>(s => s.RevokedAt != null),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RevokeShareAsync_NotOwner_ReturnsForbidden()
    {
        var share = TestData.CreateShare(createdByUserId: "other-user");
        _shareRepoMock.Setup(x => x.GetByIdAsync(share.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var currentUser = new CurrentUser(TestUserId, isSystemAdmin: false);
        var service = CreateAuthenticatedService(currentUser);
        var result = await service.RevokeShareAsync(share.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task RevokeShareAsync_ShareNotFound_Returns404()
    {
        _shareRepoMock.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Share?)null);

        var currentUser = new CurrentUser(TestUserId, isSystemAdmin: false);
        var service = CreateAuthenticatedService(currentUser);
        var result = await service.RevokeShareAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UpdateSharePasswordAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateSharePasswordAsync_Owner_Success()
    {
        var share = TestData.CreateShare(createdByUserId: TestUserId);
        _shareRepoMock.Setup(x => x.GetByIdAsync(share.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var currentUser = new CurrentUser(TestUserId, isSystemAdmin: false);
        var service = CreateAuthenticatedService(currentUser);
        var result = await service.UpdateSharePasswordAsync(share.Id, "new-password", CancellationToken.None);

        Assert.True(result.IsSuccess);
        _shareRepoMock.Verify(x => x.UpdateAsync(
            It.Is<Share>(s => !string.IsNullOrEmpty(s.PasswordHash)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSharePasswordAsync_Admin_CanUpdateOthersShare()
    {
        var share = TestData.CreateShare(createdByUserId: "other-user");
        _shareRepoMock.Setup(x => x.GetByIdAsync(share.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var adminUser = new CurrentUser(TestUserId, isSystemAdmin: true);
        var service = CreateAuthenticatedService(adminUser);
        var result = await service.UpdateSharePasswordAsync(share.Id, "admin-new-password", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateSharePasswordAsync_NotOwnerNotAdmin_ReturnsForbidden()
    {
        var share = TestData.CreateShare(createdByUserId: "other-user");
        _shareRepoMock.Setup(x => x.GetByIdAsync(share.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var currentUser = new CurrentUser(TestUserId, isSystemAdmin: false);
        var service = CreateAuthenticatedService(currentUser);
        var result = await service.UpdateSharePasswordAsync(share.Id, "new-password", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateSharePasswordAsync_EmptyPassword_ReturnsBadRequest()
    {
        var share = TestData.CreateShare(createdByUserId: TestUserId);
        _shareRepoMock.Setup(x => x.GetByIdAsync(share.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var currentUser = new CurrentUser(TestUserId, isSystemAdmin: false);
        var service = CreateAuthenticatedService(currentUser);
        var result = await service.UpdateSharePasswordAsync(share.Id, "", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EnqueueDownloadAllAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnqueueDownloadAllAsync_AssetShare_ReturnsBadRequest()
    {
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(
            scopeType: ShareScopeType.Asset, 
            scopeId: asset.Id);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        var result = await service.EnqueueDownloadAllAsync(token, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("collection shares", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnqueueDownloadAllAsync_CollectionShare_EnqueuesZip()
    {
        var collection = TestData.CreateCollection();
        var (share, token) = TestData.CreateShareWithToken(
            scopeType: ShareScopeType.Collection,
            scopeId: collection.Id);

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _collectionRepoMock.Setup(x => x.GetByIdAsync(collection.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _zipBuildServiceMock.Setup(x => x.EnqueueShareZipAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZipDownloadEnqueuedResponse { JobId = Guid.NewGuid() });

        var service = CreatePublicService();
        var result = await service.EnqueueDownloadAllAsync(token, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _zipBuildServiceMock.Verify(x => x.EnqueueShareZipAsync(
            collection.Id,
            share.TokenHash,
            collection.Name,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expiry Boundary Condition Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSharedContentAsync_ExpiresExactlyNow_ReturnsExpired()
    {
        var asset = TestData.CreateAsset();
        // Share that expires at this exact moment
        var (share, token) = TestData.CreateShareWithToken(
            scopeId: asset.Id,
            expiresAt: DateTime.UtcNow.AddSeconds(-1));

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, null, 0, 50, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("expired", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSharedContentAsync_ExpiresInFuture_Succeeds()
    {
        var asset = TestData.CreateAsset();
        var (share, token) = TestData.CreateShareWithToken(
            scopeId: asset.Id,
            expiresAt: DateTime.UtcNow.AddHours(1));

        _shareRepoMock.Setup(x => x.GetByTokenHashAsync(share.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _assetRepoMock.Setup(x => x.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset);

        var service = CreatePublicService();
        var result = await service.GetSharedContentAsync(token, null, 0, 50, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
