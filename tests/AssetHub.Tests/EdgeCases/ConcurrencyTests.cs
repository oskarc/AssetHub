using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.EdgeCases;

/// <summary>
/// Tests for concurrent operations and race conditions.
/// Verifies data integrity under parallel access patterns.
/// </summary>
[Collection("Database")]
public class ConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private string _dbName = null!;

    private const string User1 = "concurrent-user-001";
    private const string User2 = "concurrent-user-002";
    private const string User3 = "concurrent-user-003";

    public ConcurrencyTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _dbName = _db.Database.GetDbConnection().Database!;
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── Concurrent Asset Deletion ────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ConcurrentDeletion_OnlyOneSucceeds()
    {
        // Arrange: Create an asset
        var asset = TestData.CreateAsset(title: "To Be Deleted");
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        var assetId = asset.Id;

        // Create multiple repos with separate contexts
        await using var db1 = _fixture.CreateDbContextForExistingDb(_dbName);
        await using var db2 = _fixture.CreateDbContextForExistingDb(_dbName);
        var cache = TestCacheHelper.CreateHybridCache();
        var repo1 = new AssetRepository(db1, cache, NullLogger<AssetRepository>.Instance);
        var repo2 = new AssetRepository(db2, cache, NullLogger<AssetRepository>.Instance);

        // Act: Delete from both contexts concurrently
        var task1 = repo1.DeleteAsync(assetId);
        var task2 = repo2.DeleteAsync(assetId);

        // Both should complete without throwing (second one is a no-op)
        await Task.WhenAll(task1, task2);

        // Assert: Asset should be deleted
        _db.ChangeTracker.Clear();
        var exists = await _db.Assets.AnyAsync(a => a.Id == assetId);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_ParallelDeletionOfMultipleAssets_AllDeleted()
    {
        // Arrange: Create 10 assets
        var assets = Enumerable.Range(0, 10)
            .Select(i => TestData.CreateAsset(title: $"Parallel Delete {i}"))
            .ToList();
        _db.Assets.AddRange(assets);
        await _db.SaveChangesAsync();
        var assetIds = assets.Select(a => a.Id).ToList();

        // Act: Delete all concurrently with separate contexts
        var deleteTasks = assetIds.Select(async id =>
        {
            await using var db = _fixture.CreateDbContextForExistingDb(_dbName);
            var cache = TestCacheHelper.CreateHybridCache();
            var repo = new AssetRepository(db, cache, NullLogger<AssetRepository>.Instance);
            await repo.DeleteAsync(id);
        });

        await Task.WhenAll(deleteTasks);

        // Assert: All assets should be deleted
        _db.ChangeTracker.Clear();
        var remaining = await _db.Assets.Where(a => assetIds.Contains(a.Id)).CountAsync();
        Assert.Equal(0, remaining);
    }

    // ── Concurrent ACL Modifications ─────────────────────────────────────────

    [Fact]
    public async Task SetAccessAsync_ConcurrentSameUser_OneWins()
    {
        // Arrange: Create a collection
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        // Create two repos with separate contexts
        await using var db1 = _fixture.CreateDbContextForExistingDb(_dbName);
        await using var db2 = _fixture.CreateDbContextForExistingDb(_dbName);
        var repo1 = new CollectionAclRepository(db1, NullLogger<CollectionAclRepository>.Instance);
        var repo2 = new CollectionAclRepository(db2, NullLogger<CollectionAclRepository>.Instance);

        // Act: Set access for the same user concurrently with different roles
        var task1 = repo1.SetAccessAsync(collection.Id, "user", User1, "viewer");
        var task2 = repo2.SetAccessAsync(collection.Id, "user", User1, "contributor");

        await Task.WhenAll(task1, task2);

        // Assert: Only one ACL should exist (one should win)
        _db.ChangeTracker.Clear();
        var acls = await _db.CollectionAcls
            .Where(a => a.CollectionId == collection.Id && a.PrincipalId == User1)
            .ToListAsync();
        Assert.Single(acls);
        // Role should be either viewer or contributor (depends on race)
        Assert.True(acls[0].Role == AclRole.Viewer || acls[0].Role == AclRole.Contributor);
    }

    [Fact]
    public async Task SetAccessAsync_ConcurrentDifferentUsers_AllSucceed()
    {
        // Arrange: Create a collection
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        var users = new[] { User1, User2, User3, "user-4", "user-5" };

        // Act: Set access for different users concurrently
        var tasks = users.Select(async (userId, i) =>
        {
            await using var db = _fixture.CreateDbContextForExistingDb(_dbName);
            var repo = new CollectionAclRepository(db, NullLogger<CollectionAclRepository>.Instance);
            await repo.SetAccessAsync(collection.Id, "user", userId, "viewer");
        });

        await Task.WhenAll(tasks);

        // Assert: All users should have ACLs
        _db.ChangeTracker.Clear();
        var aclCount = await _db.CollectionAcls
            .Where(a => a.CollectionId == collection.Id)
            .CountAsync();
        Assert.Equal(users.Length, aclCount);
    }

    [Fact]
    public async Task SetAndRevokeAccess_ConcurrentOperations_DataIntegrity()
    {
        // Arrange: Create a collection with some ACLs
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, User1, AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, User2, AclRole.Contributor));
        await _db.SaveChangesAsync();

        // Act: Concurrently revoke User1 and set User3
        await using var db1 = _fixture.CreateDbContextForExistingDb(_dbName);
        await using var db2 = _fixture.CreateDbContextForExistingDb(_dbName);
        var repo1 = new CollectionAclRepository(db1, NullLogger<CollectionAclRepository>.Instance);
        var repo2 = new CollectionAclRepository(db2, NullLogger<CollectionAclRepository>.Instance);

        var revokeTask = repo1.RevokeAccessAsync(collection.Id, "user", User1);
        var setTask = repo2.SetAccessAsync(collection.Id, "user", User3, "manager");

        await Task.WhenAll(revokeTask, setTask);

        // Assert: User1 should be revoked, User2 unchanged, User3 added
        _db.ChangeTracker.Clear();
        var acls = await _db.CollectionAcls
            .Where(a => a.CollectionId == collection.Id)
            .ToDictionaryAsync(a => a.PrincipalId);

        Assert.False(acls.ContainsKey(User1)); // Revoked
        Assert.True(acls.ContainsKey(User2)); // Unchanged
        Assert.True(acls.ContainsKey(User3)); // Added
        Assert.Equal(AclRole.Manager, acls[User3].Role);
    }

    // ── Concurrent Share Access Counting ─────────────────────────────────────

    [Fact]
    public async Task IncrementAccessAsync_ConcurrentIncrements_AllCounted()
    {
        // Arrange: Create a share
        var share = TestData.CreateShare();
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();
        var shareId = share.Id;

        const int concurrentRequests = 20;

        // Act: Increment access count concurrently
        var incrementTasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            await using var db = _fixture.CreateDbContextForExistingDb(_dbName);
            var repo = new ShareRepository(db, NullLogger<ShareRepository>.Instance);
            await repo.IncrementAccessAsync(shareId);
        });

        await Task.WhenAll(incrementTasks);

        // Assert: Access count should equal number of concurrent requests
        _db.ChangeTracker.Clear();
        var updatedShare = await _db.Shares.FindAsync(shareId);
        Assert.Equal(concurrentRequests, updatedShare!.AccessCount);
    }

    [Fact]
    public async Task IncrementAccessAsync_HighConcurrency_NoLostUpdates()
    {
        // Arrange: Create a share
        var share = TestData.CreateShare();
        _db.Shares.Add(share);
        await _db.SaveChangesAsync();
        var shareId = share.Id;

        const int concurrentRequests = 100;

        // Act: Increment access count with high concurrency
        var incrementTasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            await using var db = _fixture.CreateDbContextForExistingDb(_dbName);
            var repo = new ShareRepository(db, NullLogger<ShareRepository>.Instance);
            await repo.IncrementAccessAsync(shareId);
        });

        await Task.WhenAll(incrementTasks);

        // Assert: No lost updates - count should be exactly 100
        _db.ChangeTracker.Clear();
        var updatedShare = await _db.Shares.FindAsync(shareId);
        Assert.Equal(concurrentRequests, updatedShare!.AccessCount);
    }

    // ── Concurrent Collection Updates ────────────────────────────────────────

    [Fact]
    public async Task UpdateCollection_ConcurrentUpdates_LastWriteWins()
    {
        // Arrange: Create a collection
        var collection = TestData.CreateCollection(name: "Original Name");
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        // Create separate contexts
        await using var db1 = _fixture.CreateDbContextForExistingDb(_dbName);
        await using var db2 = _fixture.CreateDbContextForExistingDb(_dbName);
        await using var db3 = _fixture.CreateDbContextForExistingDb(_dbName);
        var repo1 = new CollectionRepository(db1, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
        var repo2 = new CollectionRepository(db2, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
        var repo3 = new CollectionRepository(db3, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);

        // Load in all contexts
        var c1 = await repo1.GetByIdAsync(collection.Id);
        var c2 = await repo2.GetByIdAsync(collection.Id);
        var c3 = await repo3.GetByIdAsync(collection.Id);

        // Act: Update concurrently with different names
        c1!.Name = "Name from Context 1";
        c2!.Name = "Name from Context 2";
        c3!.Name = "Name from Context 3";

        var task1 = repo1.UpdateAsync(c1);
        var task2 = repo2.UpdateAsync(c2);
        var task3 = repo3.UpdateAsync(c3);

        await Task.WhenAll(task1, task2, task3);

        // Assert: Collection should exist with one of the names (last write wins)
        _db.ChangeTracker.Clear();
        var final = await _db.Collections.FindAsync(collection.Id);
        Assert.NotNull(final);
        Assert.Contains(final.Name, new[] { "Name from Context 1", "Name from Context 2", "Name from Context 3" });
    }

    // ── Concurrent Asset-Collection Linking ──────────────────────────────────

    [Fact]
    public async Task AddToCollectionAsync_ConcurrentSameAssetSameCollection_OnlyOneLink()
    {
        // Arrange: Create an asset and collection
        var asset = TestData.CreateAsset();
        var collection = TestData.CreateCollection();
        _db.Assets.Add(asset);
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();

        // Create separate contexts
        var cache = TestCacheHelper.CreateHybridCache();
        await using var db1 = _fixture.CreateDbContextForExistingDb(_dbName);
        await using var db2 = _fixture.CreateDbContextForExistingDb(_dbName);
        var repo1 = new AssetCollectionRepository(db1, cache, NullLogger<AssetCollectionRepository>.Instance);
        var repo2 = new AssetCollectionRepository(db2, cache, NullLogger<AssetCollectionRepository>.Instance);

        // Act: Try to add the same asset to the same collection from two contexts
        var task1 = repo1.AddToCollectionAsync(asset.Id, collection.Id, User1);
        var task2 = repo2.AddToCollectionAsync(asset.Id, collection.Id, User2);

        // One will succeed, one will return null (already exists)
        var results = await Task.WhenAll(task1, task2);

        // Assert: Only one link should exist
        _db.ChangeTracker.Clear();
        var links = await _db.AssetCollections
            .Where(ac => ac.AssetId == asset.Id && ac.CollectionId == collection.Id)
            .CountAsync();
        Assert.Equal(1, links);

        // One result should be non-null (success), one should be null (already linked)
        Assert.Equal(1, results.Count(r => r != null));
    }

    [Fact]
    public async Task AddToCollectionAsync_ConcurrentSameAssetDifferentCollections_AllLinked()
    {
        // Arrange: Create an asset and multiple collections
        var asset = TestData.CreateAsset();
        var collections = Enumerable.Range(0, 5)
            .Select(i => TestData.CreateCollection(name: $"Collection {i}"))
            .ToList();
        _db.Assets.Add(asset);
        _db.Collections.AddRange(collections);
        await _db.SaveChangesAsync();

        var cache = TestCacheHelper.CreateHybridCache();

        // Act: Add asset to all collections concurrently
        var tasks = collections.Select(async c =>
        {
            await using var db = _fixture.CreateDbContextForExistingDb(_dbName);
            var repo = new AssetCollectionRepository(db, cache, NullLogger<AssetCollectionRepository>.Instance);
            return await repo.AddToCollectionAsync(asset.Id, c.Id, User1);
        });

        var results = await Task.WhenAll(tasks);

        // Assert: All links should be created
        _db.ChangeTracker.Clear();
        var links = await _db.AssetCollections
            .Where(ac => ac.AssetId == asset.Id)
            .CountAsync();
        Assert.Equal(collections.Count, links);
        Assert.All(results, r => Assert.NotNull(r));
    }

    // ── Concurrent Delete and Access ─────────────────────────────────────────

    [Fact]
    public async Task DeleteAndRead_ConcurrentOperations_NoExceptions()
    {
        // Arrange: Create an asset
        var asset = TestData.CreateAsset();
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        var assetId = asset.Id;

        var cache = TestCacheHelper.CreateHybridCache();

        // Act: Concurrently delete and try to read
        await using var dbDelete = _fixture.CreateDbContextForExistingDb(_dbName);
        await using var dbRead = _fixture.CreateDbContextForExistingDb(_dbName);
        var repoDelete = new AssetRepository(dbDelete, cache, NullLogger<AssetRepository>.Instance);
        var repoRead = new AssetRepository(dbRead, cache, NullLogger<AssetRepository>.Instance);

        var deleteTask = repoDelete.DeleteAsync(assetId);
        var readTask = repoRead.GetByIdAsync(assetId);

        // Both should complete without throwing
        await Task.WhenAll(deleteTask, readTask);

        // Read result may be the asset or null (depends on timing)
        var readResult = await readTask;
        Assert.True(readResult is null || readResult.Id == assetId);

        // After completion, the asset should be deleted
        _db.ChangeTracker.Clear();
        var exists = await _db.Assets.AnyAsync(a => a.Id == assetId);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteByCollectionAsync_ConcurrentWithAssetRead_HandlesGracefully()
    {
        // Arrange: Create collection with assets
        var collection = TestData.CreateCollection();
        var assets = Enumerable.Range(0, 5)
            .Select(i => TestData.CreateAsset(title: $"Asset {i}"))
            .ToList();
        _db.Collections.Add(collection);
        _db.Assets.AddRange(assets);
        foreach (var a in assets)
            _db.AssetCollections.Add(TestData.CreateAssetCollection(a.Id, collection.Id));
        await _db.SaveChangesAsync();

        var assetIds = assets.Select(a => a.Id).ToList();
        var cache = TestCacheHelper.CreateHybridCache();

        // Act: Delete collection assets while concurrently reading them
        await using var dbDelete = _fixture.CreateDbContextForExistingDb(_dbName);
        var repoDelete = new AssetRepository(dbDelete, cache, NullLogger<AssetRepository>.Instance);

        var deleteTask = repoDelete.DeleteByCollectionAsync(collection.Id);

        var readTasks = assetIds.Select(async id =>
        {
            await using var db = _fixture.CreateDbContextForExistingDb(_dbName);
            var repo = new AssetRepository(db, cache, NullLogger<AssetRepository>.Instance);
            return await repo.GetByIdAsync(id);
        });

        // All tasks should complete without exceptions
        var allTasks = readTasks.Append(deleteTask.ContinueWith(_ => (Asset?)null));
        await Task.WhenAll(allTasks);

        // After completion, all assets should be deleted
        _db.ChangeTracker.Clear();
        var remaining = await _db.Assets.Where(a => assetIds.Contains(a.Id)).CountAsync();
        Assert.Equal(0, remaining);
    }
}
