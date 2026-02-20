using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for AssetDeletionService — orchestration of MinIO cleanup + DB deletion.
/// Uses real DB via Testcontainers, mocked MinIO adapter.
/// </summary>
[Collection("Database")]
public class AssetDeletionServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _assetRepo = null!;
    private AssetCollectionRepository _assetCollectionRepo = null!;
    private ShareRepository _shareRepo = null!;
    private Mock<IMinIOAdapter> _minioMock = null!;
    private AssetDeletionService _sut = null!;

    private const string BucketName = "test-bucket";

    public AssetDeletionServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _assetRepo = new AssetRepository(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        _assetCollectionRepo = new AssetCollectionRepository(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AssetCollectionRepository>.Instance);
        _shareRepo = new ShareRepository(_db);
        _minioMock = new Mock<IMinIOAdapter>();

        _sut = new AssetDeletionService(_assetRepo, _assetCollectionRepo, _shareRepo, _minioMock.Object);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── PermanentDeleteAsync ────────────────────────────────────────

    [Fact]
    public async Task PermanentDelete_RemovesAssetSharesAndDbRow()
    {
        var col = TestData.CreateCollection(name: "C1");
        var asset = TestData.CreateAsset(title: "ToDelete");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.Shares.Add(TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: asset.Id, createdByUserId: "u1"));
        await _db.SaveChangesAsync();

        await _sut.PermanentDeleteAsync(asset, BucketName);

        // Asset gone
        var found = await _assetRepo.GetByIdAsync(asset.Id);
        Assert.Null(found);

        // Share gone
        var shares = await _shareRepo.GetByScopeAsync(Constants.ScopeTypes.Asset, asset.Id);
        Assert.Empty(shares);

        // MinIO called
        _minioMock.Verify(m => m.DeleteAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task PermanentDelete_CallsMinIODeleteForAssetObjects()
    {
        var asset = TestData.CreateAsset(title: "MinIOTarget");
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        await _sut.PermanentDeleteAsync(asset, BucketName);

        // Should clean up original + any thumbnails
        _minioMock.Verify(m => m.DeleteAsync(BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ── RemoveFromCollectionAsync ───────────────────────────────────

    [Fact]
    public async Task RemoveFromCollection_OnlyCollection_PermanentlyDeletes()
    {
        var col = TestData.CreateCollection(name: "OnlyCol");
        var asset = TestData.CreateAsset(title: "SingleRef");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        await _db.SaveChangesAsync();

        var (removed, permanentlyDeleted) = await _sut.RemoveFromCollectionAsync(asset, col.Id, BucketName);

        Assert.True(removed);
        Assert.True(permanentlyDeleted);

        // Asset should be gone from DB
        var found = await _assetRepo.GetByIdAsync(asset.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task RemoveFromCollection_MultipleCollections_OnlyRemovesLink()
    {
        var col1 = TestData.CreateCollection(name: "Col1");
        var col2 = TestData.CreateCollection(name: "Col2");
        var asset = TestData.CreateAsset(title: "MultiRef");
        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        await _db.SaveChangesAsync();

        var (removed, permanentlyDeleted) = await _sut.RemoveFromCollectionAsync(asset, col1.Id, BucketName);

        Assert.True(removed);
        Assert.False(permanentlyDeleted);

        // Asset still exists
        var found = await _assetRepo.GetByIdAsync(asset.Id);
        Assert.NotNull(found);

        // Only the col1 link is gone
        var remainingIds = await _assetCollectionRepo.GetCollectionIdsForAssetAsync(asset.Id);
        Assert.Single(remainingIds);
        Assert.Equal(col2.Id, remainingIds.First());
    }

    [Fact]
    public async Task RemoveFromCollection_AssetNotInCollection_ReturnsNotRemoved()
    {
        var col = TestData.CreateCollection(name: "Empty");
        var asset = TestData.CreateAsset(title: "Elsewhere");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var (removed, permanentlyDeleted) = await _sut.RemoveFromCollectionAsync(asset, col.Id, BucketName);

        Assert.False(removed);
        Assert.False(permanentlyDeleted);
    }

    // ── DeleteCollectionAssetsAsync ─────────────────────────────────

    [Fact]
    public async Task DeleteCollectionAssets_DeletesAllAssetsInCollection()
    {
        var col = TestData.CreateCollection(name: "BulkDelete");
        var asset1 = TestData.CreateAsset(title: "A1");
        var asset2 = TestData.CreateAsset(title: "A2");
        _db.Collections.Add(col);
        _db.Assets.AddRange(asset1, asset2);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset1.Id, col.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset2.Id, col.Id));
        await _db.SaveChangesAsync();

        var deleted = await _sut.DeleteCollectionAssetsAsync(col.Id, BucketName);

        Assert.Equal(2, deleted.Count);
        Assert.Null(await _assetRepo.GetByIdAsync(asset1.Id));
        Assert.Null(await _assetRepo.GetByIdAsync(asset2.Id));
    }

    [Fact]
    public async Task DeleteCollectionAssets_CleansUpSharesPerAsset()
    {
        var col = TestData.CreateCollection(name: "WithShares");
        var asset = TestData.CreateAsset(title: "Shared");
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id));
        _db.Shares.Add(TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: asset.Id, createdByUserId: "u1"));
        await _db.SaveChangesAsync();

        await _sut.DeleteCollectionAssetsAsync(col.Id, BucketName);

        var shares = await _shareRepo.GetByScopeAsync(Constants.ScopeTypes.Asset, asset.Id);
        Assert.Empty(shares);
    }

    [Fact]
    public async Task DeleteCollectionAssets_EmptyCollection_ReturnsEmptyList()
    {
        var col = TestData.CreateCollection(name: "EmptyCol");
        _db.Collections.Add(col);
        await _db.SaveChangesAsync();

        var deleted = await _sut.DeleteCollectionAssetsAsync(col.Id, BucketName);

        Assert.Empty(deleted);
    }
}
