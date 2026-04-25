using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

public class AssetVersionServiceTests
{
    private readonly Mock<IAssetVersionRepository> _versionRepo = new();
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepo = new();
    private readonly Mock<ICollectionAuthorizationService> _authService = new();
    private readonly Mock<IMinIOAdapter> _minio = new();
    private readonly Mock<IAuditService> _audit = new();

    private AssetVersionService CreateService(string userId = "alice", bool isAdmin = false)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        var minioSettings = Options.Create(new MinIOSettings { BucketName = "test-bucket" });
        return new AssetVersionService(
            _versionRepo.Object,
            _assetRepo.Object,
            _assetCollectionRepo.Object,
            _authService.Object,
            _minio.Object,
            _audit.Object,
            new PassThroughUnitOfWork(),
            currentUser,
            minioSettings,
            NullLogger<AssetVersionService>.Instance);
    }

    private void SetupAccess(Guid assetId, string role)
    {
        var cid = Guid.NewGuid();
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { cid });
        _authService.Setup(a => a.FilterAccessibleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), role, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { cid });
    }

    private static AssetVersion MakeVersion(Guid assetId, int versionNumber, string originalKey = "originals/old.jpg") => new()
    {
        Id = Guid.NewGuid(),
        AssetId = assetId,
        VersionNumber = versionNumber,
        OriginalObjectKey = originalKey,
        ThumbObjectKey = $"thumbs/v{versionNumber}.jpg",
        MediumObjectKey = $"medium/v{versionNumber}.jpg",
        SizeBytes = 1024,
        ContentType = "image/jpeg",
        Sha256 = $"hash-v{versionNumber}",
        EditDocument = $"{{\"v\":{versionNumber}}}",
        MetadataSnapshot = new Dictionary<string, object> { ["snap"] = versionNumber },
        CreatedByUserId = "uploader",
        CreatedAt = DateTime.UtcNow.AddDays(-versionNumber)
    };

    // ── GetForAssetAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetForAssetAsync_AssetNotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);

        var result = await svc.GetForAssetAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetForAssetAsync_NoCollectionAccess_ReturnsForbidden()
    {
        var svc = CreateService();
        var asset = TestData.CreateAsset();
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var result = await svc.GetForAssetAsync(asset.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetForAssetAsync_MarksCurrentVersionWithIsCurrentFlag()
    {
        var svc = CreateService();
        var asset = TestData.CreateAsset();
        asset.CurrentVersionNumber = 3;
        SetupAccess(asset.Id, RoleHierarchy.Roles.Viewer);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _versionRepo.Setup(r => r.GetByAssetIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssetVersion>
            {
                MakeVersion(asset.Id, 3),
                MakeVersion(asset.Id, 2),
                MakeVersion(asset.Id, 1)
            });

        var result = await svc.GetForAssetAsync(asset.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.True(result.Value.Single(v => v.VersionNumber == 3).IsCurrent);
        Assert.False(result.Value.Single(v => v.VersionNumber == 2).IsCurrent);
        Assert.False(result.Value.Single(v => v.VersionNumber == 1).IsCurrent);
    }

    // ── RestoreAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RestoreAsync_RequiresContributorAccess()
    {
        var svc = CreateService();
        var asset = TestData.CreateAsset();
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { Guid.NewGuid() });
        _authService.Setup(a => a.FilterAccessibleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), RoleHierarchy.Roles.Contributor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var result = await svc.RestoreAsync(asset.Id, 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task RestoreAsync_VersionMissing_ReturnsNotFound()
    {
        var svc = CreateService();
        var asset = TestData.CreateAsset();
        SetupAccess(asset.Id, RoleHierarchy.Roles.Contributor);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _versionRepo.Setup(r => r.GetAsync(asset.Id, 99, It.IsAny<CancellationToken>())).ReturnsAsync((AssetVersion?)null);

        var result = await svc.RestoreAsync(asset.Id, 99, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task RestoreAsync_CapturesCurrentToNewVersionThenWritesTargetOntoAsset()
    {
        var svc = CreateService(userId: "alice");
        var asset = TestData.CreateAsset();
        asset.CurrentVersionNumber = 3;
        asset.OriginalObjectKey = "originals/current.jpg";
        asset.SizeBytes = 9999;
        var target = MakeVersion(asset.Id, 1, originalKey: "originals/v1.jpg");
        target.SizeBytes = 1111;

        SetupAccess(asset.Id, RoleHierarchy.Roles.Contributor);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _versionRepo.Setup(r => r.GetAsync(asset.Id, 1, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        AssetVersion? captured = null;
        _versionRepo.Setup(r => r.CreateAsync(It.IsAny<AssetVersion>(), It.IsAny<CancellationToken>()))
            .Callback<AssetVersion, CancellationToken>((v, _) => captured = v)
            .ReturnsAsync((AssetVersion v, CancellationToken _) => v);

        var result = await svc.RestoreAsync(asset.Id, 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Snapshot captured before mutation: VersionNumber = oldCurrent + 1, keys = old asset state
        Assert.NotNull(captured);
        Assert.Equal(4, captured!.VersionNumber);
        Assert.Equal("originals/current.jpg", captured.OriginalObjectKey);
        Assert.Equal(9999, captured.SizeBytes);
        // Asset row now reflects the chosen version
        Assert.Equal("originals/v1.jpg", asset.OriginalObjectKey);
        Assert.Equal(1111, asset.SizeBytes);
        Assert.Equal(4, asset.CurrentVersionNumber);
        // Audit logged
        _audit.Verify(a => a.LogAsync("asset.version_restored", Constants.ScopeTypes.Asset, asset.Id, "alice",
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── PruneAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task PruneAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.PruneAsync(Guid.NewGuid(), 1, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task PruneAsync_RefusesToPruneCurrentVersion()
    {
        var svc = CreateService(isAdmin: true);
        var asset = TestData.CreateAsset();
        asset.CurrentVersionNumber = 5;
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var result = await svc.PruneAsync(asset.Id, 5, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _versionRepo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PruneAsync_SkipsKeysStillReferencedByLiveAsset()
    {
        // Restored-then-prune scenario: the asset is currently pointing at v1's keys, and admin
        // is trying to prune v1. The version row goes; the bytes must NOT be deleted because the
        // live asset still references them.
        var svc = CreateService(isAdmin: true);
        var sharedKey = "originals/shared.jpg";
        var asset = TestData.CreateAsset();
        asset.CurrentVersionNumber = 3;
        asset.OriginalObjectKey = sharedKey;
        var target = MakeVersion(asset.Id, 1, originalKey: sharedKey);

        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _versionRepo.Setup(r => r.GetAsync(asset.Id, 1, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await svc.PruneAsync(asset.Id, 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _minio.Verify(m => m.DeleteAsync("test-bucket", sharedKey, It.IsAny<CancellationToken>()), Times.Never);
        _versionRepo.Verify(r => r.DeleteAsync(target.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PruneAsync_DeletesOrphanKeysAndAuditsResult()
    {
        var svc = CreateService(isAdmin: true);
        var asset = TestData.CreateAsset();
        asset.CurrentVersionNumber = 3;
        asset.OriginalObjectKey = "originals/current.jpg";
        var target = MakeVersion(asset.Id, 1, originalKey: "originals/v1.jpg");

        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _versionRepo.Setup(r => r.GetAsync(asset.Id, 1, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var result = await svc.PruneAsync(asset.Id, 1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Original + thumb + medium are all v1-specific keys, so all three deleted
        _minio.Verify(m => m.DeleteAsync("test-bucket", "originals/v1.jpg", It.IsAny<CancellationToken>()), Times.Once);
        _minio.Verify(m => m.DeleteAsync("test-bucket", "thumbs/v1.jpg", It.IsAny<CancellationToken>()), Times.Once);
        _minio.Verify(m => m.DeleteAsync("test-bucket", "medium/v1.jpg", It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync("asset.version_pruned", Constants.ScopeTypes.Asset, asset.Id, It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
