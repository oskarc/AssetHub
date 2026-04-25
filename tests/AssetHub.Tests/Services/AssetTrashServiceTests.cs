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

public class AssetTrashServiceTests
{
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly Mock<IAssetDeletionService> _deletionService = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IWebhookEventPublisher> _webhooks = new();

    private AssetTrashService CreateService(string userId = "admin-001", bool isAdmin = true, int retentionDays = 30)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        var lifecycle = Options.Create(new AssetLifecycleSettings { TrashRetentionDays = retentionDays });
        var minio = Options.Create(new MinIOSettings { BucketName = "test-bucket" });
        return new AssetTrashService(
            _assetRepo.Object,
            _deletionService.Object,
            _audit.Object,
            _webhooks.Object,
            new PassThroughUnitOfWork(),
            currentUser,
            lifecycle,
            minio,
            NullLogger<AssetTrashService>.Instance);
    }

    private static Asset MakeTrashed(DateTime? deletedAt = null, string deletedBy = "alice")
    {
        var asset = TestData.CreateAsset();
        asset.DeletedAt = deletedAt ?? DateTime.UtcNow.AddDays(-1);
        asset.DeletedByUserId = deletedBy;
        return asset;
    }

    // ── GetAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.GetAsync(0, 50, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ProjectsAssetsToTrashedAssetDtoWithExpiresAt()
    {
        var svc = CreateService(retentionDays: 7);
        var deletedAt = DateTime.UtcNow.AddDays(-2);
        var asset = MakeTrashed(deletedAt);
        _assetRepo.Setup(r => r.GetTrashAsync(0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Asset> { asset }, 1));

        var result = await svc.GetAsync(0, 50, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(asset.Id, item.Id);
        // Expires = DeletedAt + 7 days
        Assert.Equal(deletedAt.AddDays(7), item.ExpiresAt);
    }

    // ── RestoreAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RestoreAsync_NotInTrash_ReturnsBadRequest()
    {
        var svc = CreateService();
        var liveAsset = TestData.CreateAsset();
        _assetRepo.Setup(r => r.GetByIdIncludingDeletedAsync(liveAsset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveAsset);

        var result = await svc.RestoreAsync(liveAsset.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task RestoreAsync_NotFound_Returns404()
    {
        var svc = CreateService();
        _assetRepo.Setup(r => r.GetByIdIncludingDeletedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Asset?)null);

        var result = await svc.RestoreAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task RestoreAsync_Trashed_CallsDeletionServiceAndAudits()
    {
        var svc = CreateService(userId: "admin-X");
        var trashed = MakeTrashed();
        _assetRepo.Setup(r => r.GetByIdIncludingDeletedAsync(trashed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trashed);

        var result = await svc.RestoreAsync(trashed.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _deletionService.Verify(d => d.RestoreAsync(trashed, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(
            "asset.restored", Constants.ScopeTypes.Asset, trashed.Id, "admin-X",
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── PurgeAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task PurgeAsync_RequiresTrashedState()
    {
        var svc = CreateService();
        var liveAsset = TestData.CreateAsset();
        _assetRepo.Setup(r => r.GetByIdIncludingDeletedAsync(liveAsset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveAsset);

        var result = await svc.PurgeAsync(liveAsset.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _deletionService.Verify(d => d.PurgeAsync(It.IsAny<Asset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PurgeAsync_Trashed_CallsDeletionServicePurgeAndAudits()
    {
        var svc = CreateService(userId: "admin-X");
        var trashed = MakeTrashed();
        _assetRepo.Setup(r => r.GetByIdIncludingDeletedAsync(trashed.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trashed);

        var result = await svc.PurgeAsync(trashed.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _deletionService.Verify(d => d.PurgeAsync(trashed, "test-bucket", It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(
            "asset.purged", Constants.ScopeTypes.Asset, trashed.Id, "admin-X",
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── EmptyAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task EmptyAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.EmptyAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task EmptyAsync_PurgesEveryTrashedAssetReturnsCounts()
    {
        var svc = CreateService();
        var trashed = new List<Asset> { MakeTrashed(), MakeTrashed(), MakeTrashed() };
        // Initial call returns the three rows; subsequent call returns empty (purged).
        _assetRepo.SetupSequence(r => r.GetTrashAsync(0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync((trashed, 3))
            .ReturnsAsync((new List<Asset>(), 0));

        var result = await svc.EmptyAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Purged);
        Assert.Equal(0, result.Value.Failed);
        _deletionService.Verify(d => d.PurgeAsync(It.IsAny<Asset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task EmptyAsync_AllPurgesFail_BailsOutWithFailedCount()
    {
        var svc = CreateService();
        var trashed = new List<Asset> { MakeTrashed(), MakeTrashed() };
        _assetRepo.Setup(r => r.GetTrashAsync(0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync((trashed, 2));
        _deletionService.Setup(d => d.PurgeAsync(It.IsAny<Asset>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("MinIO unreachable"));

        var result = await svc.EmptyAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Purged);
        Assert.Equal(2, result.Value.Failed);
    }
}
