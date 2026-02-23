using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.EdgeCases;

/// <summary>
/// Tests for smart multi-collection-aware deletion logic per V2 plan §10.6.
/// </summary>
[Collection("Database")]
public class SmartDeletionTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _assetRepo = null!;

    public SmartDeletionTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _assetRepo = new AssetRepository(_db, new MemoryCache(new MemoryCacheOptions()), NullLogger<AssetRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── DeleteByCollectionAsync: exclusive vs shared ────────────────

    [Fact]
    public async Task DeleteByCollection_ExclusiveAsset_IsDeleted()
    {
        var collection = TestData.CreateCollection(name: "OnlyCollection");
        var asset = TestData.CreateAsset(title: "Exclusive Asset");

        _db.Collections.Add(collection);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        await _db.SaveChangesAsync();

        var deleted = await _assetRepo.DeleteByCollectionAsync(collection.Id);

        Assert.Single(deleted);
        Assert.Equal(asset.Id, deleted[0].Id);
        Assert.Null(await _db.Assets.FindAsync(asset.Id));
        Assert.Empty(await _db.AssetCollections.Where(ac => ac.CollectionId == collection.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteByCollection_SharedAsset_IsPreserved()
    {
        var col1 = TestData.CreateCollection(name: "Col1");
        var col2 = TestData.CreateCollection(name: "Col2");
        var sharedAsset = TestData.CreateAsset(title: "Shared Asset");

        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(sharedAsset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(sharedAsset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(sharedAsset.Id, col2.Id));
        await _db.SaveChangesAsync();

        var deleted = await _assetRepo.DeleteByCollectionAsync(col1.Id);

        // Shared asset should NOT be deleted
        Assert.Empty(deleted);
        Assert.NotNull(await _db.Assets.FindAsync(sharedAsset.Id));

        // Link to col1 removed, link to col2 preserved
        var remainingLinks = await _db.AssetCollections
            .Where(ac => ac.AssetId == sharedAsset.Id).ToListAsync();
        Assert.Single(remainingLinks);
        Assert.Equal(col2.Id, remainingLinks[0].CollectionId);
    }

    [Fact]
    public async Task DeleteByCollection_MixedAssets_OnlyExclusiveDeleted()
    {
        var col1 = TestData.CreateCollection(name: "Target");
        var col2 = TestData.CreateCollection(name: "Other");
        var exclusiveAsset = TestData.CreateAsset(title: "Exclusive");
        var sharedAsset = TestData.CreateAsset(title: "Shared");

        _db.Collections.AddRange(col1, col2);
        _db.Assets.AddRange(exclusiveAsset, sharedAsset);
        // exclusive only in col1
        _db.AssetCollections.Add(TestData.CreateAssetCollection(exclusiveAsset.Id, col1.Id));
        // shared in both col1 and col2
        _db.AssetCollections.Add(TestData.CreateAssetCollection(sharedAsset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(sharedAsset.Id, col2.Id));
        await _db.SaveChangesAsync();

        var deleted = await _assetRepo.DeleteByCollectionAsync(col1.Id);

        // Only exclusive asset should be returned as deleted
        Assert.Single(deleted);
        Assert.Equal(exclusiveAsset.Id, deleted[0].Id);

        // Exclusive asset removed from DB
        Assert.Null(await _db.Assets.FindAsync(exclusiveAsset.Id));

        // Shared asset still exists and accessible from col2
        Assert.NotNull(await _db.Assets.FindAsync(sharedAsset.Id));
        var remainingLinks = await _db.AssetCollections
            .Where(ac => ac.AssetId == sharedAsset.Id).ToListAsync();
        Assert.Single(remainingLinks);
        Assert.Equal(col2.Id, remainingLinks[0].CollectionId);
    }

    [Fact]
    public async Task DeleteByCollection_SharedAssetIn3Collections_PreservedIn2()
    {
        var col1 = TestData.CreateCollection(name: "Delete Me");
        var col2 = TestData.CreateCollection(name: "Keep 1");
        var col3 = TestData.CreateCollection(name: "Keep 2");
        var asset = TestData.CreateAsset(title: "Multi-shared");

        _db.Collections.AddRange(col1, col2, col3);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col3.Id));
        await _db.SaveChangesAsync();

        var deleted = await _assetRepo.DeleteByCollectionAsync(col1.Id);

        Assert.Empty(deleted);
        Assert.NotNull(await _db.Assets.FindAsync(asset.Id));
        var remainingLinks = await _db.AssetCollections
            .Where(ac => ac.AssetId == asset.Id).ToListAsync();
        Assert.Equal(2, remainingLinks.Count);
        Assert.All(remainingLinks, link => Assert.NotEqual(col1.Id, link.CollectionId));
    }

    [Fact]
    public async Task DeleteByCollection_EmptyCollection_ReturnsEmpty()
    {
        var collection = TestData.CreateCollection(name: "Empty");
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var deleted = await _assetRepo.DeleteByCollectionAsync(collection.Id);

        Assert.Empty(deleted);
    }

    [Fact]
    public async Task DeleteByCollection_MultipleExclusiveAssets_AllDeleted()
    {
        var collection = TestData.CreateCollection(name: "Full Col");
        var asset1 = TestData.CreateAsset(title: "A1");
        var asset2 = TestData.CreateAsset(title: "A2");
        var asset3 = TestData.CreateAsset(title: "A3");

        _db.Collections.Add(collection);
        _db.Assets.AddRange(asset1, asset2, asset3);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset1.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset2.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset3.Id, collection.Id));
        await _db.SaveChangesAsync();

        var deleted = await _assetRepo.DeleteByCollectionAsync(collection.Id);

        Assert.Equal(3, deleted.Count);
        Assert.Null(await _db.Assets.FindAsync(asset1.Id));
        Assert.Null(await _db.Assets.FindAsync(asset2.Id));
        Assert.Null(await _db.Assets.FindAsync(asset3.Id));
    }
}
