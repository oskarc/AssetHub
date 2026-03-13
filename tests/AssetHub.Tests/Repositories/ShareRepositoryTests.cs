using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Repositories;

[Collection("Database")]
public class ShareRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private ShareRepository _repo = null!;

    public ShareRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _repo = new ShareRepository(_db, NullLogger<ShareRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── GetByIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsShare()
    {
        var share = TestData.CreateShare();
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(share.Id);

        Assert.NotNull(result);
        Assert.Equal(share.Id, result.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    // ── GetByTokenHashAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetByTokenHashAsync_FindsShare()
    {
        var share = TestData.CreateShare(tokenHash: "unique_token_hash");
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();

        var result = await _repo.GetByTokenHashAsync("unique_token_hash");

        Assert.NotNull(result);
        Assert.Equal(share.Id, result.Id);
    }

    [Fact]
    public async Task GetByTokenHashAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.GetByTokenHashAsync("nonexistent_hash");
        Assert.Null(result);
    }

    // ── GetByScopeAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetByScopeAsync_ReturnsSharesForScope()
    {
        var assetId = Guid.NewGuid();
        var share1 = TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: assetId);
        var share2 = TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: assetId);
        var otherShare = TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: Guid.NewGuid());

        _db.Shares.AddRange(share1, share2, otherShare);
        await _db.SaveChangesAsync();

        var results = await _repo.GetByScopeAsync(ShareScopeType.Asset.ToDbString(), assetId);

        Assert.Equal(2, results.Count);
        Assert.All(results, s => Assert.Equal(assetId, s.ScopeId));
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsShare()
    {
        var share = TestData.CreateShare();

        await _repo.CreateAsync(share);

        var found = await _db.Shares.FindAsync(share.Id);
        Assert.NotNull(found);
        Assert.Equal(share.TokenHash, found.TokenHash);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ModifiesFields()
    {
        var share = TestData.CreateShare();
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();

        share.RevokedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(share);

        var found = await _db.Shares.FindAsync(share.Id);
        Assert.NotNull(found!.RevokedAt);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesShare()
    {
        var share = TestData.CreateShare();
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();

        await _repo.DeleteAsync(share.Id);

        Assert.Null(await _db.Shares.FindAsync(share.Id));
    }

    [Fact]
    public async Task DeleteAsync_NoOp_WhenNotExists()
    {
        var ex = await Record.ExceptionAsync(() => _repo.DeleteAsync(Guid.NewGuid()));
        Assert.Null(ex);
    }

    // ── IncrementAccessAsync (atomic SQL UPDATE) ────────────────────

    [Fact]
    public async Task IncrementAccessAsync_IncrementsCountAndSetsLastAccessed()
    {
        var share = TestData.CreateShare();
        share.AccessCount = 0;
        share.LastAccessedAt = null;
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();

        await _repo.IncrementAccessAsync(share.Id);

        // Detach and re-fetch to see updated values
        _db.ChangeTracker.Clear();
        var updated = await _db.Shares.FindAsync(share.Id);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.AccessCount);
        Assert.NotNull(updated.LastAccessedAt);
    }

    [Fact]
    public async Task IncrementAccessAsync_IncrementsMultipleTimes()
    {
        var share = TestData.CreateShare();
        share.AccessCount = 5;
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();

        await _repo.IncrementAccessAsync(share.Id);
        await _repo.IncrementAccessAsync(share.Id);
        await _repo.IncrementAccessAsync(share.Id);

        _db.ChangeTracker.Clear();
        var updated = await _db.Shares.FindAsync(share.Id);
        Assert.Equal(8, updated!.AccessCount);
    }

    // ── GetByUserAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByUserAsync_ReturnsOnlyUserShares()
    {
        _db.Shares.Add(TestData.CreateShare(createdByUserId: "user1"));
        _db.Shares.Add(TestData.CreateShare(createdByUserId: "user1"));
        _db.Shares.Add(TestData.CreateShare(createdByUserId: "user2"));
        await _db.SaveChangesAsync();

        var results = await _repo.GetByUserAsync("user1");

        Assert.Equal(2, results.Count);
        Assert.All(results, s => Assert.Equal("user1", s.CreatedByUserId));
    }

    [Fact]
    public async Task GetByUserAsync_SupportsPagination()
    {
        for (int i = 0; i < 10; i++)
        {
            var share = TestData.CreateShare(createdByUserId: "pager");
            share.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            _db.Shares.Add(share);
        }
        await _db.SaveChangesAsync();

        var page = await _repo.GetByUserAsync("pager", skip: 2, take: 3);
        Assert.Equal(3, page.Count);
    }

    // ── GetAllAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllShares()
    {
        _db.Shares.Add(TestData.CreateShare());
        _db.Shares.Add(TestData.CreateShare());
        _db.Shares.Add(TestData.CreateShare());
        await _db.SaveChangesAsync();

        var all = await _repo.GetAllAsync();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task GetAllAsync_WithIncludeAsset_LoadsAssetNavigation()
    {
        var asset = TestData.CreateAsset(title: "Shared Asset");
        _db.Assets.Add(asset);
        var share = TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: asset.Id);
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();

        var results = await _repo.GetAllAsync(new ShareQueryOptions(IncludeAsset: true));

        Assert.Single(results);
        Assert.NotNull(results[0].Asset);
        Assert.Equal("Shared Asset", results[0].Asset!.Title);
    }

    [Fact]
    public async Task GetAllAsync_WithIncludeCollection_LoadsCollectionNavigation()
    {
        var collection = TestData.CreateCollection(name: "Shared Collection");
        _db.Collections.Add(collection);
        var share = TestData.CreateShare(scopeType: ShareScopeType.Collection, scopeId: collection.Id);
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();

        var results = await _repo.GetAllAsync(new ShareQueryOptions(IncludeCollection: true));

        Assert.Single(results);
        Assert.NotNull(results[0].Collection);
        Assert.Equal("Shared Collection", results[0].Collection!.Name);
    }

    // ── Sort order verification ─────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_SortsByCreatedAtDescending()
    {
        var oldest = TestData.CreateShare();
        oldest.CreatedAt = DateTime.UtcNow.AddDays(-3);
        var middle = TestData.CreateShare();
        middle.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var newest = TestData.CreateShare();
        newest.CreatedAt = DateTime.UtcNow.AddDays(-1);

        _db.Shares.AddRange(oldest, middle, newest);
        await _db.SaveChangesAsync();

        var results = await _repo.GetAllAsync();

        Assert.Equal(3, results.Count);
        Assert.True(results[0].CreatedAt > results[1].CreatedAt);
        Assert.True(results[1].CreatedAt > results[2].CreatedAt);
        Assert.Equal(newest.Id, results[0].Id);
        Assert.Equal(oldest.Id, results[2].Id);
    }

    [Fact]
    public async Task GetByUserAsync_SortsByCreatedAtDescending()
    {
        var oldest = TestData.CreateShare(createdByUserId: "sort-user");
        oldest.CreatedAt = DateTime.UtcNow.AddDays(-3);
        var middle = TestData.CreateShare(createdByUserId: "sort-user");
        middle.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var newest = TestData.CreateShare(createdByUserId: "sort-user");
        newest.CreatedAt = DateTime.UtcNow.AddDays(-1);

        _db.Shares.AddRange(oldest, middle, newest);
        await _db.SaveChangesAsync();

        var results = await _repo.GetByUserAsync("sort-user");

        Assert.Equal(3, results.Count);
        Assert.Equal(newest.Id, results[0].Id);
        Assert.Equal(middle.Id, results[1].Id);
        Assert.Equal(oldest.Id, results[2].Id);
    }

    [Fact]
    public async Task GetByUserAsync_ReturnsEmpty_ForUnknownUser()
    {
        _db.Shares.Add(TestData.CreateShare(createdByUserId: "known-user"));
        await _db.SaveChangesAsync();

        var results = await _repo.GetByUserAsync("unknown-user");

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetByScopeAsync_SortsByCreatedAtDescending()
    {
        var scopeId = Guid.NewGuid();
        var oldest = TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: scopeId);
        oldest.CreatedAt = DateTime.UtcNow.AddDays(-3);
        var middle = TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: scopeId);
        middle.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var newest = TestData.CreateShare(scopeType: ShareScopeType.Asset, scopeId: scopeId);
        newest.CreatedAt = DateTime.UtcNow.AddDays(-1);

        _db.Shares.AddRange(oldest, middle, newest);
        await _db.SaveChangesAsync();

        var results = await _repo.GetByScopeAsync(ShareScopeType.Asset.ToDbString(), scopeId);

        Assert.Equal(3, results.Count);
        Assert.Equal(newest.Id, results[0].Id);
        Assert.Equal(middle.Id, results[1].Id);
        Assert.Equal(oldest.Id, results[2].Id);
    }

    // ── CountAllAsync / pagination ────────────────────────────────────────────

    [Fact]
    public async Task CountAllAsync_ReturnsCorrectCount()
    {
        _db.Shares.Add(TestData.CreateShare());
        _db.Shares.Add(TestData.CreateShare());
        await _db.SaveChangesAsync();

        var count = await _repo.CountAllAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetAllAsync_RespectsSkipAndTake()
    {
        // Add 5 shares
        for (var i = 0; i < 5; i++)
            _db.Shares.Add(TestData.CreateShare());
        await _db.SaveChangesAsync();

        var page1 = await _repo.GetAllAsync(new ShareQueryOptions(Skip: 0, Take: 3));
        var page2 = await _repo.GetAllAsync(new ShareQueryOptions(Skip: 3, Take: 3));

        Assert.Equal(3, page1.Count);
        Assert.Equal(2, page2.Count);
        // No overlap
        Assert.Empty(page1.Select(s => s.Id).Intersect(page2.Select(s => s.Id)));
    }
}
