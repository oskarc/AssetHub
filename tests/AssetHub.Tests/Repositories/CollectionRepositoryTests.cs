using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Repositories;

[Collection("Database")]
public class CollectionRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private CollectionRepository _repo = null!;

    public CollectionRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _repo = new CollectionRepository(_db, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── GetByIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsCollection()
    {
        var collection = TestData.CreateCollection(name: "My Collection");
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(collection.Id);

        Assert.NotNull(result);
        Assert.Equal("My Collection", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_IncludesAcls_WhenRequested()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user2", AclRole.Contributor));
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync(collection.Id, includeAcls: true);

        Assert.NotNull(result);
        Assert.Equal(2, result.Acls.Count);
    }

    [Fact]
    public async Task GetByIdAsync_DoesNotIncludeAcls_ByDefault()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Viewer));
        await _db.SaveChangesAsync();

        // Use a fresh context to avoid change-tracker populating the navigation
        var dbName = _db.Database.GetDbConnection().Database!;
        await using var freshDb = _fixture.CreateDbContextForExistingDb(dbName);
        var freshRepo = new CollectionRepository(freshDb, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);

        var result = await freshRepo.GetByIdAsync(collection.Id);

        Assert.NotNull(result);
        // Navigation not loaded — default empty collection
        Assert.Empty(result.Acls);
    }

    // ── GetRootCollectionsAsync ─────────────────────────────────────

    [Fact]
    public async Task GetRootCollectionsAsync_OrdersByName()
    {
        _db.Collections.Add(TestData.CreateCollection(name: "Zebra"));
        _db.Collections.Add(TestData.CreateCollection(name: "Alpha"));
        _db.Collections.Add(TestData.CreateCollection(name: "Middle"));
        await _db.SaveChangesAsync();

        var roots = (await _repo.GetRootCollectionsAsync()).ToList();

        Assert.Equal("Alpha", roots[0].Name);
        Assert.Equal("Middle", roots[1].Name);
        Assert.Equal("Zebra", roots[2].Name);
    }

    // ── GetAccessibleCollectionsAsync ───────────────────────────────

    [Fact]
    public async Task GetAccessibleCollectionsAsync_ReturnsOnlyUserCollections()
    {
        var accessible = TestData.CreateCollection(name: "Accessible");
        var notAccessible = TestData.CreateCollection(name: "Not Accessible");
        _db.Collections.AddRange(accessible, notAccessible);

        _db.CollectionAcls.Add(TestData.CreateAcl(accessible.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(notAccessible.Id, "user2", AclRole.Viewer));
        await _db.SaveChangesAsync();

        var result = (await _repo.GetAccessibleCollectionsAsync("user1")).ToList();

        Assert.Single(result);
        Assert.Equal("Accessible", result[0].Name);
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PersistsCollection()
    {
        var collection = TestData.CreateCollection(name: "New");

        var created = await _repo.CreateAsync(collection);

        Assert.NotEqual(Guid.Empty, created.Id);
        var found = await _db.Collections.FindAsync(created.Id);
        Assert.NotNull(found);
        Assert.Equal("New", found.Name);
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedAt()
    {
        var collection = TestData.CreateCollection();
        collection.CreatedAt = default;

        var created = await _repo.CreateAsync(collection);

        Assert.NotEqual(default, created.CreatedAt);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ModifiesName()
    {
        var collection = TestData.CreateCollection(name: "Original");
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        collection.Name = "Renamed";
        await _repo.UpdateAsync(collection);

        var found = await _db.Collections.FindAsync(collection.Id);
        Assert.Equal("Renamed", found!.Name);
    }

    // ── DeleteAsync (recursive) ─────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesCollection()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        await _repo.DeleteAsync(collection.Id);

        Assert.Null(await _db.Collections.FindAsync(collection.Id));
    }

    [Fact]
    public async Task DeleteAsync_CascadesAcls()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Viewer));
        await _db.SaveChangesAsync();

        await _repo.DeleteAsync(collection.Id);

        var remainingAcls = await _db.CollectionAcls.Where(a => a.CollectionId == collection.Id).ToListAsync();
        Assert.Empty(remainingAcls);
    }

    // ── ExistsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenExists()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        Assert.True(await _repo.ExistsAsync(collection.Id));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenNotExists()
    {
        Assert.False(await _repo.ExistsAsync(Guid.NewGuid()));
    }

    // ── GetAllWithAclsAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetAllWithAclsAsync_ReturnsCollectionsWithAcls()
    {
        var col1 = TestData.CreateCollection(name: "C1");
        var col2 = TestData.CreateCollection(name: "C2");
        _db.Collections.AddRange(col1, col2);
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, "user1", AclRole.Admin));
        await _db.SaveChangesAsync();

        var all = (await _repo.GetAllWithAclsAsync()).ToList();

        Assert.Equal(2, all.Count);
        var withAcl = all.First(c => c.Id == col1.Id);
        Assert.Single(withAcl.Acls);
    }
}
