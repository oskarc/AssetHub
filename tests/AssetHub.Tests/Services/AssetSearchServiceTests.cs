using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for AssetSearchService against real Postgres with migrations applied.
/// Covers the pieces that can only be meaningfully tested with triggers active: tsvector-backed
/// full-text search, ACL filtering, filter dimensions, and facet-count accuracy.
/// </summary>
[Collection("Database")]
public class AssetSearchServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionRepository _collectionRepo = null!;

    public AssetSearchServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateMigratedDbContextAsync();
        _collectionRepo = new CollectionRepository(_db, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private AssetSearchService CreateService(string userId, bool isAdmin)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        return new AssetSearchService(_db, _collectionRepo, currentUser, NullLogger<AssetSearchService>.Instance);
    }

    /// <summary>Seeds a collection the given user can see, then inserts assets into it.</summary>
    private async Task<(Collection Collection, List<Asset> Assets)> SeedCollectionWithAssetsAsync(
        string userId, params Asset[] assets)
    {
        var collection = TestData.CreateCollection(name: $"col-{Guid.NewGuid():N}");
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, userId, AclRole.Viewer));
        foreach (var a in assets)
        {
            _db.Assets.Add(a);
            _db.AssetCollections.Add(TestData.CreateAssetCollection(a.Id, collection.Id));
        }
        await _db.SaveChangesAsync();
        return (collection, assets.ToList());
    }

    // ── RBAC ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_NonAdmin_OnlySeesAssetsInAccessibleCollections()
    {
        var userId = "alice";
        var mine = TestData.CreateAsset(title: "Mine");
        var notMine = TestData.CreateAsset(title: "NotMine");

        await SeedCollectionWithAssetsAsync(userId, mine);

        // A second collection Alice has NO ACL for, holding the NotMine asset.
        var hiddenCollection = TestData.CreateCollection(name: "hidden");
        _db.Collections.Add(hiddenCollection);
        _db.Assets.Add(notMine);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(notMine.Id, hiddenCollection.Id));
        await _db.SaveChangesAsync();

        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(new AssetSearchRequest { Take = 50 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Mine", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_Admin_SeesAssetsAcrossAllCollections()
    {
        var alice = TestData.CreateAsset(title: "AssetA");
        var bob = TestData.CreateAsset(title: "AssetB");

        // Two collections, neither one with an ACL for the calling admin — admin bypasses ACL.
        var c1 = TestData.CreateCollection(name: "a");
        var c2 = TestData.CreateCollection(name: "b");
        _db.Collections.AddRange(c1, c2);
        _db.Assets.AddRange(alice, bob);
        _db.AssetCollections.AddRange(
            TestData.CreateAssetCollection(alice.Id, c1.Id),
            TestData.CreateAssetCollection(bob.Id, c2.Id));
        await _db.SaveChangesAsync();

        var svc = CreateService("admin-001", isAdmin: true);
        var result = await svc.SearchAsync(new AssetSearchRequest { Take = 50 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_CollectionFilterWithInaccessibleId_ReturnsEmpty()
    {
        var userId = "alice";
        var asset = TestData.CreateAsset(title: "Mine");
        await SeedCollectionWithAssetsAsync(userId, asset);

        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(
            new AssetSearchRequest { CollectionIds = new() { Guid.NewGuid() } },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalCount);
    }

    // ── Full-text search (exercises triggers + tsvector) ─────────────

    [Fact]
    public async Task SearchAsync_Text_MatchesTitleViaTsVector()
    {
        var userId = "alice";
        await SeedCollectionWithAssetsAsync(userId,
            TestData.CreateAsset(title: "Forest sunrise"),
            TestData.CreateAsset(title: "Urban rooftop"));

        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(
            new AssetSearchRequest { Text = "forest" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Forest sunrise", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_Text_MatchesTagsViaTsVector()
    {
        var userId = "alice";
        await SeedCollectionWithAssetsAsync(userId,
            TestData.CreateAsset(title: "One", tags: new() { "seasonal", "outdoor" }),
            TestData.CreateAsset(title: "Two", tags: new() { "studio" }));

        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(
            new AssetSearchRequest { Text = "outdoor" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("One", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_Text_PicksUpSearchableMetadataValuesViaTriggers()
    {
        var userId = "alice";
        var asset = TestData.CreateAsset(title: "Campaign");
        await SeedCollectionWithAssetsAsync(userId, asset);

        // Add a searchable metadata field with a value that's unique to the asset.
        var field = TestData.CreateMetadataField(key: "campaign_code", type: MetadataFieldType.Text);
        field.Searchable = true;
        var schema = TestData.CreateMetadataSchema(name: "test-schema", fields: new() { field });
        _db.MetadataSchemas.Add(schema);
        _db.AssetMetadataValues.Add(TestData.CreateAssetMetadataValue(
            assetId: asset.Id, fieldId: field.Id, valueText: "moonshot-42"));
        await _db.SaveChangesAsync();

        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(
            new AssetSearchRequest { Text = "moonshot" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal(asset.Id, result.Value.Items[0].Id);
    }

    // ── Filter dimensions ────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_AssetTypeFilter_NarrowsResults()
    {
        var userId = "alice";
        await SeedCollectionWithAssetsAsync(userId,
            TestData.CreateAsset(title: "Photo", assetType: AssetType.Image),
            TestData.CreateAsset(title: "Clip", assetType: AssetType.Video));

        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(
            new AssetSearchRequest { AssetTypes = new() { "image" } },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Photo", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task SearchAsync_UploadingAssets_AreExcluded()
    {
        var userId = "alice";
        await SeedCollectionWithAssetsAsync(userId,
            TestData.CreateAsset(title: "Ready", status: AssetStatus.Ready),
            TestData.CreateAsset(title: "Uploading", status: AssetStatus.Uploading));

        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(new AssetSearchRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Ready", result.Value.Items[0].Title);
    }

    // ── Facets ───────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_AssetTypeFacet_ExcludesOwnDimensionForAddAnotherUx()
    {
        var userId = "alice";
        await SeedCollectionWithAssetsAsync(userId,
            TestData.CreateAsset(title: "P1", assetType: AssetType.Image),
            TestData.CreateAsset(title: "P2", assetType: AssetType.Image),
            TestData.CreateAsset(title: "V1", assetType: AssetType.Video));

        // Filter by Image but still ask for the asset_type facet — buckets should count across
        // the set WITHOUT the asset-type filter applied, so the user sees "2 images, 1 video".
        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(new AssetSearchRequest
        {
            AssetTypes = new() { "image" },
            Facets = new() { "asset_type" }
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);   // main query still filters to images
        Assert.True(result.Value.Facets.ContainsKey("asset_type"));
        var buckets = result.Value.Facets["asset_type"];
        Assert.Contains(buckets, b => b.Value == "image" && b.Count == 2);
        Assert.Contains(buckets, b => b.Value == "video" && b.Count == 1);
    }

    [Fact]
    public async Task SearchAsync_TagFacet_CountsDistinctTagsAcrossResultSet()
    {
        var userId = "alice";
        await SeedCollectionWithAssetsAsync(userId,
            TestData.CreateAsset(title: "A", tags: new() { "red", "blue" }),
            TestData.CreateAsset(title: "B", tags: new() { "red" }),
            TestData.CreateAsset(title: "C", tags: new() { "blue", "green" }));

        var svc = CreateService(userId, isAdmin: false);
        var result = await svc.SearchAsync(new AssetSearchRequest
        {
            Facets = new() { "tag" }
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var tagBuckets = result.Value!.Facets["tag"];
        Assert.Equal(2, tagBuckets.First(b => b.Value == "red").Count);
        Assert.Equal(2, tagBuckets.First(b => b.Value == "blue").Count);
        Assert.Equal(1, tagBuckets.First(b => b.Value == "green").Count);
    }

    // ── Pagination ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_SkipTake_PagesResults()
    {
        var userId = "alice";
        var assets = Enumerable.Range(0, 5)
            .Select(i => TestData.CreateAsset(title: $"Asset-{i:D2}"))
            .ToArray();
        await SeedCollectionWithAssetsAsync(userId, assets);

        var svc = CreateService(userId, isAdmin: false);
        var page1 = await svc.SearchAsync(new AssetSearchRequest { Take = 2, Skip = 0, Sort = "title_asc" }, CancellationToken.None);
        var page2 = await svc.SearchAsync(new AssetSearchRequest { Take = 2, Skip = 2, Sort = "title_asc" }, CancellationToken.None);

        Assert.Equal(5, page1.Value!.TotalCount);
        Assert.Equal("Asset-00", page1.Value.Items[0].Title);
        Assert.Equal("Asset-01", page1.Value.Items[1].Title);
        Assert.Equal("Asset-02", page2.Value!.Items[0].Title);
        Assert.Equal("Asset-03", page2.Value.Items[1].Title);
    }
}
