using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Repositories;

[Collection("Database")]
public class AssetCollectionRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetCollectionRepository _repo = null!;

    public AssetCollectionRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        var cache = TestCacheHelper.CreateHybridCache();
        var logger = NullLogger<AssetCollectionRepository>.Instance;
        _repo = new AssetCollectionRepository(_fixture.CreateDbContextProvider(dbName), cache, logger);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── GetCollectionsForAssetAsync ─────────────────────────────────

    [Fact]
    public async Task GetCollectionsForAssetAsync_ReturnsCollections()
    {
        var col1 = TestData.CreateCollection(name: "Col1");
        var col2 = TestData.CreateCollection(name: "Col2");
        var col3 = TestData.CreateCollection(name: "Col3");
        var asset = TestData.CreateAsset();

        _db.Collections.AddRange(col1, col2, col3);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        await _db.SaveChangesAsync();

        var collections = await _repo.GetCollectionsForAssetAsync(asset.Id);

        Assert.Equal(2, collections.Count);
        Assert.Contains(collections, c => c.Name == "Col1");
        Assert.Contains(collections, c => c.Name == "Col2");
        Assert.DoesNotContain(collections, c => c.Name == "Col3");
    }

    // ── GetByAssetAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetByAssetAsync_ReturnsJoinEntitiesWithCollection()
    {
        var col1 = TestData.CreateCollection(name: "Col1");
        var col2 = TestData.CreateCollection(name: "Col2");
        var asset = TestData.CreateAsset();

        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        await _db.SaveChangesAsync();

        var results = await _repo.GetByAssetAsync(asset.Id);

        Assert.Equal(2, results.Count);
        // Verify Collection navigation is included
        Assert.All(results, ac => Assert.NotNull(ac.Collection));
        Assert.Contains(results, ac => ac.Collection.Name == "Col1");
        Assert.Contains(results, ac => ac.Collection.Name == "Col2");
    }

    [Fact]
    public async Task GetByAssetAsync_ReturnsEmpty_WhenNoCollections()
    {
        var asset = TestData.CreateAsset();
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var results = await _repo.GetByAssetAsync(asset.Id);

        Assert.Empty(results);
    }

    // ── GetByCollectionAsync ────────────────────────────────────────

    [Fact]
    public async Task GetByCollectionAsync_ReturnsJoinEntitiesWithAsset()
    {
        var collection = TestData.CreateCollection();
        var asset1 = TestData.CreateAsset(title: "A1");
        var asset2 = TestData.CreateAsset(title: "A2");

        _db.Collections.Add(collection);
        _db.Assets.AddRange(asset1, asset2);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset1.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset2.Id, collection.Id));
        await _db.SaveChangesAsync();

        var results = await _repo.GetByCollectionAsync(collection.Id);

        Assert.Equal(2, results.Count);
        // Verify Asset navigation is included
        Assert.All(results, ac => Assert.NotNull(ac.Asset));
        Assert.Contains(results, ac => ac.Asset.Title == "A1");
        Assert.Contains(results, ac => ac.Asset.Title == "A2");
    }

    [Fact]
    public async Task GetByCollectionAsync_ReturnsEmpty_WhenNoAssets()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var results = await _repo.GetByCollectionAsync(collection.Id);

        Assert.Empty(results);
    }

    // ── AddToCollectionAsync ────────────────────────────────────────

    [Fact]
    public async Task AddToCollectionAsync_CreatesJoinEntry()
    {
        var collection = TestData.CreateCollection();
        var asset = TestData.CreateAsset();
        _db.Collections.Add(collection);
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var result = await _repo.AddToCollectionAsync(asset.Id, collection.Id, "user1");

        Assert.NotNull(result);
        Assert.Equal(asset.Id, result.AssetId);
        Assert.Equal(collection.Id, result.CollectionId);
        Assert.True(await _repo.BelongsToCollectionAsync(asset.Id, collection.Id));
    }

    [Fact]
    public async Task AddToCollectionAsync_ReturnsNull_WhenAlreadyLinked()
    {
        var collection = TestData.CreateCollection();
        var asset = TestData.CreateAsset();
        _db.Collections.Add(collection);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        await _db.SaveChangesAsync();

        var result = await _repo.AddToCollectionAsync(asset.Id, collection.Id, "user1");

        Assert.Null(result); // duplicate prevented
    }

    [Fact]
    public async Task AddToCollectionAsync_ReturnsNull_WhenAssetNotExists()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var result = await _repo.AddToCollectionAsync(Guid.NewGuid(), collection.Id, "user1");
        Assert.Null(result);
    }

    [Fact]
    public async Task AddToCollectionAsync_ReturnsNull_WhenCollectionNotExists()
    {
        var asset = TestData.CreateAsset();
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var result = await _repo.AddToCollectionAsync(asset.Id, Guid.NewGuid(), "user1");
        Assert.Null(result);
    }

    // ── RemoveFromCollectionAsync ───────────────────────────────────

    [Fact]
    public async Task RemoveFromCollectionAsync_RemovesLink()
    {
        var collection = TestData.CreateCollection();
        var asset = TestData.CreateAsset();
        _db.Collections.Add(collection);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        await _db.SaveChangesAsync();

        var removed = await _repo.RemoveFromCollectionAsync(asset.Id, collection.Id);

        Assert.True(removed);
        Assert.False(await _repo.BelongsToCollectionAsync(asset.Id, collection.Id));
    }

    [Fact]
    public async Task RemoveFromCollectionAsync_ReturnsFalse_WhenNotLinked()
    {
        var result = await _repo.RemoveFromCollectionAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(result);
    }

    // ── BelongsToCollectionAsync ────────────────────────────────────

    [Fact]
    public async Task BelongsToCollectionAsync_ReturnsTrue_WhenInCollection()
    {
        var collection = TestData.CreateCollection();
        var asset = TestData.CreateAsset();
        _db.Collections.Add(collection);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        await _db.SaveChangesAsync();

        Assert.True(await _repo.BelongsToCollectionAsync(asset.Id, collection.Id));
    }

    [Fact]
    public async Task BelongsToCollectionAsync_ReturnsFalse_WhenNotInCollection()
    {
        var collection = TestData.CreateCollection();
        var asset = TestData.CreateAsset();
        _db.Collections.Add(collection);
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        Assert.False(await _repo.BelongsToCollectionAsync(asset.Id, collection.Id));
    }

    // ── GetCollectionIdsForAssetAsync ───────────────────────────────

    [Fact]
    public async Task GetCollectionIdsForAssetAsync_ReturnsAllCollectionIds()
    {
        var col1 = TestData.CreateCollection(name: "C1");
        var col2 = TestData.CreateCollection(name: "C2");
        var asset = TestData.CreateAsset();

        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        await _db.SaveChangesAsync();

        var ids = await _repo.GetCollectionIdsForAssetAsync(asset.Id);

        Assert.Equal(2, ids.Count);
        Assert.Contains(col1.Id, ids);
        Assert.Contains(col2.Id, ids);
    }

    [Fact]
    public async Task GetCollectionIdsForAssetAsync_CachesResult()
    {
        var collection = TestData.CreateCollection();
        var asset = TestData.CreateAsset();
        _db.Collections.Add(collection);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        await _db.SaveChangesAsync();

        // First call — populates cache
        var ids1 = await _repo.GetCollectionIdsForAssetAsync(asset.Id);
        // Second call — should hit cache (same result)
        var ids2 = await _repo.GetCollectionIdsForAssetAsync(asset.Id);

        Assert.Equal(ids1, ids2);
    }

    // ── GetCollectionIdsForAssetsAsync (batch) ──────────────────────

    [Fact]
    public async Task GetCollectionIdsForAssetsAsync_ReturnsBatchResults()
    {
        var col1 = TestData.CreateCollection(name: "C1");
        var col2 = TestData.CreateCollection(name: "C2");
        var asset1 = TestData.CreateAsset();
        var asset2 = TestData.CreateAsset();

        _db.Collections.AddRange(col1, col2);
        _db.Assets.AddRange(asset1, asset2);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset1.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset2.Id, col2.Id));
        await _db.SaveChangesAsync();

        var result = await _repo.GetCollectionIdsForAssetsAsync(new[] { asset1.Id, asset2.Id });

        Assert.Equal(2, result.Count);
        Assert.Contains(col1.Id, result[asset1.Id]);
        Assert.Contains(col2.Id, result[asset2.Id]);
    }
}
