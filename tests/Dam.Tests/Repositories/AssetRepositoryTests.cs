using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Dam.Infrastructure.Repositories;
using Dam.Tests.Fixtures;
using Dam.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Dam.Tests.Repositories;

[Collection("Database")]
public class AssetRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _repo = null!;

    public AssetRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _repo = new AssetRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── GetByIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsAsset_WhenExists()
    {
        var asset = TestData.CreateAsset(title: "Find Me");
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(asset.Id);

        Assert.NotNull(result);
        Assert.Equal(asset.Id, result.Id);
        Assert.Equal("Find Me", result.Title);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    // ── GetByCollectionAsync ────────────────────────────────────────

    [Fact]
    public async Task GetByCollectionAsync_ReturnsAssetsInCollection()
    {
        var collection = TestData.CreateCollection();
        var asset1 = TestData.CreateAsset(title: "In Collection");
        var asset2 = TestData.CreateAsset(title: "Also In Collection");
        var assetOther = TestData.CreateAsset(title: "Not In Collection");

        _db.Collections.Add(collection);
        _db.Assets.AddRange(asset1, asset2, assetOther);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset1.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset2.Id, collection.Id));
        await _db.SaveChangesAsync();

        var results = await _repo.GetByCollectionAsync(collection.Id);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, a => a.Title == "In Collection");
        Assert.Contains(results, a => a.Title == "Also In Collection");
        Assert.DoesNotContain(results, a => a.Title == "Not In Collection");
    }

    [Fact]
    public async Task GetByCollectionAsync_SupportsPagination()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        for (int i = 0; i < 10; i++)
        {
            var asset = TestData.CreateAsset(title: $"Asset {i:D2}");
            asset.CreatedAt = DateTime.UtcNow.AddMinutes(-i); // deterministic order
            _db.Assets.Add(asset);
            _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        }
        await _db.SaveChangesAsync();

        var page = await _repo.GetByCollectionAsync(collection.Id, skip: 3, take: 4);

        Assert.Equal(4, page.Count);
    }

    // ── CountByCollectionAsync ──────────────────────────────────────

    [Fact]
    public async Task CountByCollectionAsync_ReturnsCorrectCount()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        for (int i = 0; i < 5; i++)
        {
            var asset = TestData.CreateAsset();
            _db.Assets.Add(asset);
            _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        }
        await _db.SaveChangesAsync();

        var count = await _repo.CountByCollectionAsync(collection.Id);
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task CountByCollectionAsync_ReturnsZero_ForEmptyCollection()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var count = await _repo.CountByCollectionAsync(collection.Id);
        Assert.Equal(0, count);
    }

    // ── CreateAsync / UpdateAsync / DeleteAsync ─────────────────────

    [Fact]
    public async Task CreateAsync_PersistsAsset()
    {
        var asset = TestData.CreateAsset(title: "New Asset");

        await _repo.CreateAsync(asset);

        var found = await _db.Assets.FindAsync(asset.Id);
        Assert.NotNull(found);
        Assert.Equal("New Asset", found.Title);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesFields()
    {
        var asset = TestData.CreateAsset(title: "Original");
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        asset.Title = "Updated";
        asset.Description = "New description";
        await _repo.UpdateAsync(asset);

        var found = await _db.Assets.FindAsync(asset.Id);
        Assert.NotNull(found);
        Assert.Equal("Updated", found.Title);
        Assert.Equal("New description", found.Description);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAsset()
    {
        var asset = TestData.CreateAsset();
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        await _repo.DeleteAsync(asset.Id);

        var found = await _db.Assets.FindAsync(asset.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteAsync_NoOp_WhenNotExists()
    {
        // Should not throw
        await _repo.DeleteAsync(Guid.NewGuid());
    }

    // ── DeleteByCollectionAsync ─────────────────────────────────────

    [Fact]
    public async Task DeleteByCollectionAsync_RemovesOnlyCollectionAssets()
    {
        var col1 = TestData.CreateCollection(name: "Col1");
        var col2 = TestData.CreateCollection(name: "Col2");
        var asset1 = TestData.CreateAsset(title: "A1");
        var asset2 = TestData.CreateAsset(title: "A2");

        _db.Collections.AddRange(col1, col2);
        _db.Assets.AddRange(asset1, asset2);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset1.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset2.Id, col2.Id));
        await _db.SaveChangesAsync();

        await _repo.DeleteByCollectionAsync(col1.Id);

        Assert.Null(await _db.Assets.FindAsync(asset1.Id));
        Assert.NotNull(await _db.Assets.FindAsync(asset2.Id));
    }

    // ── SearchAsync (uses ILike — requires real PostgreSQL) ─────────

    [Fact]
    public async Task SearchAsync_FindsByTitle()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var match = TestData.CreateAsset(title: "Sunset at the Beach");
        var noMatch = TestData.CreateAsset(title: "Mountain View");
        _db.Assets.AddRange(match, noMatch);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(match.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(noMatch.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAsync(collection.Id, query: "sunset");

        Assert.Equal(1, total);
        Assert.Single(results);
        Assert.Equal("Sunset at the Beach", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_FindsByDescription()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var match = TestData.CreateAsset(title: "Photo", description: "A beautiful sunset photo");
        var noMatch = TestData.CreateAsset(title: "Video", description: "Mountain hiking video");
        _db.Assets.AddRange(match, noMatch);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(match.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(noMatch.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAsync(collection.Id, query: "sunset");

        Assert.Equal(1, total);
        Assert.Equal("Photo", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_IsCaseInsensitive()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var asset = TestData.CreateAsset(title: "UPPERCASE TITLE");
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, _) = await _repo.SearchAsync(collection.Id, query: "uppercase");

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_FiltersByAssetType()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var image = TestData.CreateAsset(title: "Photo", assetType: Asset.TypeImage);
        var video = TestData.CreateAsset(title: "Clip", assetType: Asset.TypeVideo);
        _db.Assets.AddRange(image, video);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(image.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(video.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAsync(collection.Id, assetType: Asset.TypeImage);

        Assert.Equal(1, total);
        Assert.Equal("Photo", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_SortsCorrectly()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var a = TestData.CreateAsset(title: "Alpha");
        var b = TestData.CreateAsset(title: "Beta");
        var c = TestData.CreateAsset(title: "Charlie");
        _db.Assets.AddRange(a, b, c);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(a.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(b.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(c.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, _) = await _repo.SearchAsync(collection.Id, sortBy: "title_asc");

        Assert.Equal("Alpha", results[0].Title);
        Assert.Equal("Beta", results[1].Title);
        Assert.Equal("Charlie", results[2].Title);
    }

    // ── SearchAllAsync (ACL filtering) ──────────────────────────────

    [Fact]
    public async Task SearchAllAsync_RespectsAllowedCollections()
    {
        var allowed = TestData.CreateCollection(name: "Allowed");
        var forbidden = TestData.CreateCollection(name: "Forbidden");
        _db.Collections.AddRange(allowed, forbidden);

        var visible = TestData.CreateAsset(title: "Visible", status: Asset.StatusReady);
        var hidden = TestData.CreateAsset(title: "Hidden", status: Asset.StatusReady);
        _db.Assets.AddRange(visible, hidden);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(visible.Id, allowed.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(hidden.Id, forbidden.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAllAsync(
            allowedCollectionIds: new List<Guid> { allowed.Id });

        Assert.Equal(1, total);
        Assert.Equal("Visible", results[0].Title);
    }

    [Fact]
    public async Task SearchAllAsync_ExcludesNonReadyAssets()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var ready = TestData.CreateAsset(title: "Ready", status: Asset.StatusReady);
        var processing = TestData.CreateAsset(title: "Processing", status: Asset.StatusProcessing);
        _db.Assets.AddRange(ready, processing);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(ready.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(processing.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAllAsync(
            allowedCollectionIds: new List<Guid> { collection.Id });

        Assert.Equal(1, total);
        Assert.Equal("Ready", results[0].Title);
    }

    // ── GetByOriginalKeyAsync ───────────────────────────────────────

    [Fact]
    public async Task GetByOriginalKeyAsync_FindsByKey()
    {
        var asset = TestData.CreateAsset();
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var result = await _repo.GetByOriginalKeyAsync(asset.OriginalObjectKey);

        Assert.NotNull(result);
        Assert.Equal(asset.Id, result.Id);
    }

    // ── GetByTypeAsync / GetByStatusAsync ───────────────────────────

    [Fact]
    public async Task GetByTypeAsync_FiltersCorrectly()
    {
        _db.Assets.Add(TestData.CreateAsset(assetType: Asset.TypeImage));
        _db.Assets.Add(TestData.CreateAsset(assetType: Asset.TypeVideo));
        _db.Assets.Add(TestData.CreateAsset(assetType: Asset.TypeImage));
        await _db.SaveChangesAsync();

        var images = await _repo.GetByTypeAsync(Asset.TypeImage);
        Assert.Equal(2, images.Count);
    }

    [Fact]
    public async Task GetByStatusAsync_FiltersCorrectly()
    {
        _db.Assets.Add(TestData.CreateAsset(status: Asset.StatusReady));
        _db.Assets.Add(TestData.CreateAsset(status: Asset.StatusProcessing));
        _db.Assets.Add(TestData.CreateAsset(status: Asset.StatusReady));
        await _db.SaveChangesAsync();

        var ready = await _repo.GetByStatusAsync(Asset.StatusReady);
        Assert.Equal(2, ready.Count);
    }

    // ── GetByUserAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByUserAsync_ReturnsAssetsForUser()
    {
        _db.Assets.Add(TestData.CreateAsset(title: "User1 Asset", createdByUserId: "user-1"));
        _db.Assets.Add(TestData.CreateAsset(title: "User2 Asset", createdByUserId: "user-2"));
        _db.Assets.Add(TestData.CreateAsset(title: "User1 Other", createdByUserId: "user-1"));
        await _db.SaveChangesAsync();

        var results = await _repo.GetByUserAsync("user-1");

        Assert.Equal(2, results.Count);
        Assert.All(results, a => Assert.Equal("user-1", a.CreatedByUserId));
    }

    [Fact]
    public async Task GetByUserAsync_SupportsPagination()
    {
        for (int i = 0; i < 10; i++)
        {
            var asset = TestData.CreateAsset(createdByUserId: "paginated-user");
            asset.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            _db.Assets.Add(asset);
        }
        await _db.SaveChangesAsync();

        var page = await _repo.GetByUserAsync("paginated-user", skip: 2, take: 3);

        Assert.Equal(3, page.Count);
    }

    [Fact]
    public async Task GetByUserAsync_ReturnsEmpty_ForUnknownUser()
    {
        _db.Assets.Add(TestData.CreateAsset(createdByUserId: "existing-user"));
        await _db.SaveChangesAsync();

        var results = await _repo.GetByUserAsync("nonexistent-user");

        Assert.Empty(results);
    }

    // ── CountByStatusAsync ──────────────────────────────────────────

    [Fact]
    public async Task CountByStatusAsync_ReturnsCorrectCount()
    {
        _db.Assets.Add(TestData.CreateAsset(status: Asset.StatusReady));
        _db.Assets.Add(TestData.CreateAsset(status: Asset.StatusReady));
        _db.Assets.Add(TestData.CreateAsset(status: Asset.StatusProcessing));
        _db.Assets.Add(TestData.CreateAsset(status: Asset.StatusFailed));
        await _db.SaveChangesAsync();

        var readyCount = await _repo.CountByStatusAsync(Asset.StatusReady);
        var processingCount = await _repo.CountByStatusAsync(Asset.StatusProcessing);
        var failedCount = await _repo.CountByStatusAsync(Asset.StatusFailed);

        Assert.Equal(2, readyCount);
        Assert.Equal(1, processingCount);
        Assert.Equal(1, failedCount);
    }

    [Fact]
    public async Task CountByStatusAsync_ReturnsZero_ForNoMatches()
    {
        _db.Assets.Add(TestData.CreateAsset(status: Asset.StatusReady));
        await _db.SaveChangesAsync();

        var count = await _repo.CountByStatusAsync(Asset.StatusFailed);

        Assert.Equal(0, count);
    }

    // ── SearchAllAsync (expanded coverage) ──────────────────────────

    [Fact]
    public async Task SearchAllAsync_FiltersByTextQuery()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var match = TestData.CreateAsset(title: "Marketing Campaign", status: Asset.StatusReady);
        var noMatch = TestData.CreateAsset(title: "Sales Report", status: Asset.StatusReady);
        _db.Assets.AddRange(match, noMatch);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(match.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(noMatch.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAllAsync(
            query: "marketing",
            allowedCollectionIds: new List<Guid> { collection.Id });

        Assert.Equal(1, total);
        Assert.Equal("Marketing Campaign", results[0].Title);
    }

    [Fact]
    public async Task SearchAllAsync_FiltersByAssetType()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var image = TestData.CreateAsset(title: "Photo", assetType: Asset.TypeImage, status: Asset.StatusReady);
        var video = TestData.CreateAsset(title: "Clip", assetType: Asset.TypeVideo, status: Asset.StatusReady);
        _db.Assets.AddRange(image, video);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(image.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(video.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAllAsync(
            assetType: Asset.TypeVideo,
            allowedCollectionIds: new List<Guid> { collection.Id });

        Assert.Equal(1, total);
        Assert.Equal("Clip", results[0].Title);
    }

    [Fact]
    public async Task SearchAllAsync_SortsByTitleAscending()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var c = TestData.CreateAsset(title: "Charlie", status: Asset.StatusReady);
        var a = TestData.CreateAsset(title: "Alpha", status: Asset.StatusReady);
        var b = TestData.CreateAsset(title: "Beta", status: Asset.StatusReady);
        _db.Assets.AddRange(c, a, b);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(c.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(a.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(b.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, _) = await _repo.SearchAllAsync(
            sortBy: "title_asc",
            allowedCollectionIds: new List<Guid> { collection.Id });

        Assert.Equal("Alpha", results[0].Title);
        Assert.Equal("Beta", results[1].Title);
        Assert.Equal("Charlie", results[2].Title);
    }

    [Fact]
    public async Task SearchAllAsync_NullAllowedCollectionIds_ReturnsAllReadyAssets()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var ready = TestData.CreateAsset(title: "Visible", status: Asset.StatusReady);
        var notReady = TestData.CreateAsset(title: "Hidden", status: Asset.StatusProcessing);
        _db.Assets.AddRange(ready, notReady);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(ready.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(notReady.Id, collection.Id));
        await _db.SaveChangesAsync();

        // Null = admin (no collection filter), but still filters by Ready status
        var (results, total) = await _repo.SearchAllAsync(allowedCollectionIds: null);

        Assert.Equal(1, total);
        Assert.Equal("Visible", results[0].Title);
    }

    [Fact]
    public async Task SearchAllAsync_EmptyAllowedCollectionIds_ReturnsNothing()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        _db.Assets.Add(TestData.CreateAsset(title: "Asset", status: Asset.StatusReady));
        await _db.SaveChangesAsync();

        // Empty list (non-null) = user has access to zero collections
        var (results, total) = await _repo.SearchAllAsync(
            allowedCollectionIds: new List<Guid>());

        Assert.Equal(0, total);
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAllAsync_SupportsPagination()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        for (int i = 0; i < 10; i++)
        {
            var asset = TestData.CreateAsset(title: $"Asset {i:D2}", status: Asset.StatusReady);
            _db.Assets.Add(asset);
            _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        }
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAllAsync(
            skip: 3, take: 4,
            allowedCollectionIds: new List<Guid> { collection.Id });

        Assert.Equal(10, total);
        Assert.Equal(4, results.Count);
    }

    // ── SearchAsync (sort order variations) ──────────────────────────

    [Fact]
    public async Task SearchAsync_SortsByTitleDescending()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var a = TestData.CreateAsset(title: "Alpha");
        var b = TestData.CreateAsset(title: "Beta");
        var c = TestData.CreateAsset(title: "Charlie");
        _db.Assets.AddRange(a, b, c);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(a.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(b.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(c.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, _) = await _repo.SearchAsync(collection.Id, sortBy: "title_desc");

        Assert.Equal("Charlie", results[0].Title);
        Assert.Equal("Beta", results[1].Title);
        Assert.Equal("Alpha", results[2].Title);
    }

    [Fact]
    public async Task SearchAsync_SortsBySizeAscending()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var small = TestData.CreateAsset(title: "Small", sizeBytes: 100);
        var medium = TestData.CreateAsset(title: "Medium", sizeBytes: 5000);
        var large = TestData.CreateAsset(title: "Large", sizeBytes: 1000000);
        _db.Assets.AddRange(small, medium, large);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(small.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(medium.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(large.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, _) = await _repo.SearchAsync(collection.Id, sortBy: "size_asc");

        Assert.Equal("Small", results[0].Title);
        Assert.Equal("Medium", results[1].Title);
        Assert.Equal("Large", results[2].Title);
    }

    [Fact]
    public async Task SearchAsync_SortsBySizeDescending()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var small = TestData.CreateAsset(title: "Small", sizeBytes: 100);
        var medium = TestData.CreateAsset(title: "Medium", sizeBytes: 5000);
        var large = TestData.CreateAsset(title: "Large", sizeBytes: 1000000);
        _db.Assets.AddRange(small, medium, large);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(small.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(medium.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(large.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, _) = await _repo.SearchAsync(collection.Id, sortBy: "size_desc");

        Assert.Equal("Large", results[0].Title);
        Assert.Equal("Medium", results[1].Title);
        Assert.Equal("Small", results[2].Title);
    }

    [Fact]
    public async Task SearchAsync_SortsByCreatedAscending()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var oldest = TestData.CreateAsset(title: "Oldest");
        oldest.CreatedAt = DateTime.UtcNow.AddDays(-3);
        var middle = TestData.CreateAsset(title: "Middle");
        middle.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var newest = TestData.CreateAsset(title: "Newest");
        newest.CreatedAt = DateTime.UtcNow.AddDays(-1);
        _db.Assets.AddRange(oldest, middle, newest);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(oldest.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(middle.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(newest.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, _) = await _repo.SearchAsync(collection.Id, sortBy: "created_asc");

        Assert.Equal("Oldest", results[0].Title);
        Assert.Equal("Middle", results[1].Title);
        Assert.Equal("Newest", results[2].Title);
    }

    [Fact]
    public async Task SearchAsync_DefaultSortIsCreatedDescending()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var old = TestData.CreateAsset(title: "Old");
        old.CreatedAt = DateTime.UtcNow.AddDays(-5);
        var recent = TestData.CreateAsset(title: "Recent");
        recent.CreatedAt = DateTime.UtcNow.AddDays(-1);
        _db.Assets.AddRange(old, recent);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(old.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(recent.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, _) = await _repo.SearchAsync(collection.Id);

        Assert.Equal("Recent", results[0].Title);
        Assert.Equal("Old", results[1].Title);
    }

    // ── SearchAsync (pagination) ────────────────────────────────────

    [Fact]
    public async Task SearchAsync_SupportsPagination_WithCorrectTotal()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        for (int i = 0; i < 15; i++)
        {
            var asset = TestData.CreateAsset(title: $"Asset {i:D2}");
            _db.Assets.Add(asset);
            _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        }
        await _db.SaveChangesAsync();

        var (page1, total1) = await _repo.SearchAsync(collection.Id, skip: 0, take: 5);
        var (page2, total2) = await _repo.SearchAsync(collection.Id, skip: 5, take: 5);
        var (page3, total3) = await _repo.SearchAsync(collection.Id, skip: 10, take: 5);

        Assert.Equal(15, total1);
        Assert.Equal(15, total2);
        Assert.Equal(15, total3);
        Assert.Equal(5, page1.Count);
        Assert.Equal(5, page2.Count);
        Assert.Equal(5, page3.Count);
        // Ensure no overlap between pages
        Assert.Empty(page1.Select(a => a.Id).Intersect(page2.Select(a => a.Id)));
        Assert.Empty(page2.Select(a => a.Id).Intersect(page3.Select(a => a.Id)));
    }

    // ── SearchAsync (combined filters) ──────────────────────────────

    [Fact]
    public async Task SearchAsync_CombinesQueryAndTypeFilter()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var matchBoth = TestData.CreateAsset(title: "Beach Sunset", assetType: Asset.TypeImage);
        var matchQuery = TestData.CreateAsset(title: "Beach Video", assetType: Asset.TypeVideo);
        var matchType = TestData.CreateAsset(title: "Mountain Photo", assetType: Asset.TypeImage);
        var noMatch = TestData.CreateAsset(title: "City Tour", assetType: Asset.TypeVideo);
        _db.Assets.AddRange(matchBoth, matchQuery, matchType, noMatch);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(matchBoth.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(matchQuery.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(matchType.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(noMatch.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAsync(collection.Id, query: "beach", assetType: Asset.TypeImage);

        Assert.Equal(1, total);
        Assert.Equal("Beach Sunset", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_CombinesQueryTypeAndSort()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        var a = TestData.CreateAsset(title: "A Beach", assetType: Asset.TypeImage, sizeBytes: 500);
        var b = TestData.CreateAsset(title: "B Beach", assetType: Asset.TypeImage, sizeBytes: 100);
        var c = TestData.CreateAsset(title: "C Beach", assetType: Asset.TypeImage, sizeBytes: 300);
        var x = TestData.CreateAsset(title: "X Beach", assetType: Asset.TypeVideo, sizeBytes: 50);
        _db.Assets.AddRange(a, b, c, x);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(a.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(b.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(c.Id, collection.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(x.Id, collection.Id));
        await _db.SaveChangesAsync();

        var (results, total) = await _repo.SearchAsync(
            collection.Id, query: "beach", assetType: Asset.TypeImage, sortBy: "size_asc");

        Assert.Equal(3, total);
        Assert.Equal("B Beach", results[0].Title);
        Assert.Equal("C Beach", results[1].Title);
        Assert.Equal("A Beach", results[2].Title);
    }

    // ── UpdateAsync (concurrent modification) ───────────────────────

    [Fact]
    public async Task UpdateAsync_ConcurrentModification_LastWriteWins()
    {
        var asset = TestData.CreateAsset(title: "Original");
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var dbName = _db.Database.GetDbConnection().Database;

        // Create second context + repo pointing to same DB
        await using var db2 = _fixture.CreateDbContextForExistingDb(dbName!);
        var repo2 = new AssetRepository(db2);

        // Load the same entity in both contexts
        var asset1 = await _repo.GetByIdAsync(asset.Id);
        var asset2 = await repo2.GetByIdAsync(asset.Id);

        // Modify in context 1
        asset1!.Title = "Update from context 1";
        await _repo.UpdateAsync(asset1);

        // Modify in context 2 (without knowing about context 1's change)
        asset2!.Title = "Update from context 2";
        await repo2.UpdateAsync(asset2);

        // Verify last write wins (no ConcurrencyToken configured)
        _db.ChangeTracker.Clear();
        var final = await _db.Assets.FindAsync(asset.Id);
        Assert.Equal("Update from context 2", final!.Title);
    }
}
