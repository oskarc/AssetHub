using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Dam.Infrastructure.Repositories;
using Dam.Tests.Fixtures;
using Dam.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dam.Tests.EdgeCases;

/// <summary>
/// Tests for multi-collection access scenarios, cascade behavior,
/// and orphaned asset edge cases per V2 plan §5.4.
/// </summary>
[Collection("Database")]
public class MultiCollectionAccessTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _assetRepo = null!;
    private AssetCollectionRepository _acRepo = null!;
    private CollectionRepository _collectionRepo = null!;
    private CollectionAclRepository _aclRepo = null!;

    public MultiCollectionAccessTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = NullLogger<AssetCollectionRepository>.Instance;
        _assetRepo = new AssetRepository(_db, cache);
        _acRepo = new AssetCollectionRepository(_db, cache, logger);
        _collectionRepo = new CollectionRepository(_db);
        _aclRepo = new CollectionAclRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── Asset in 2+ collections ─────────────────────────────────────

    [Fact]
    public async Task AssetInMultipleCollections_CanBeFoundFromEach()
    {
        var col1 = TestData.CreateCollection(name: "Col1");
        var col2 = TestData.CreateCollection(name: "Col2");
        var asset = TestData.CreateAsset(title: "Shared Asset");

        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        await _db.SaveChangesAsync();

        var fromCol1 = await _assetRepo.GetByCollectionAsync(col1.Id);
        var fromCol2 = await _assetRepo.GetByCollectionAsync(col2.Id);

        Assert.Single(fromCol1);
        Assert.Single(fromCol2);
        Assert.Equal(asset.Id, fromCol1[0].Id);
        Assert.Equal(asset.Id, fromCol2[0].Id);
    }

    [Fact]
    public async Task AssetInMultipleCollections_CollectionIdsReturnsAll()
    {
        var col1 = TestData.CreateCollection(name: "C1");
        var col2 = TestData.CreateCollection(name: "C2");
        var col3 = TestData.CreateCollection(name: "C3");
        var asset = TestData.CreateAsset();

        _db.Collections.AddRange(col1, col2, col3);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col3.Id));
        await _db.SaveChangesAsync();

        var collectionIds = await _acRepo.GetCollectionIdsForAssetAsync(asset.Id);

        Assert.Equal(3, collectionIds.Count);
    }

    // ── Removing from one collection doesn't affect others ──────────

    [Fact]
    public async Task RemoveFromOneCollection_DoesNotAffectOtherCollections()
    {
        var col1 = TestData.CreateCollection(name: "Col1");
        var col2 = TestData.CreateCollection(name: "Col2");
        var asset = TestData.CreateAsset();

        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        await _db.SaveChangesAsync();

        // Remove from col1
        await _acRepo.RemoveFromCollectionAsync(asset.Id, col1.Id);

        // Still in col2
        Assert.False(await _acRepo.BelongsToCollectionAsync(asset.Id, col1.Id));
        Assert.True(await _acRepo.BelongsToCollectionAsync(asset.Id, col2.Id));

        // Asset itself still exists
        Assert.NotNull(await _assetRepo.GetByIdAsync(asset.Id));
    }

    [Fact]
    public async Task RemoveFromAllCollections_AssetBecomesOrphaned()
    {
        var col1 = TestData.CreateCollection(name: "Col1");
        var col2 = TestData.CreateCollection(name: "Col2");
        var asset = TestData.CreateAsset();

        _db.Collections.AddRange(col1, col2);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col1.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col2.Id));
        await _db.SaveChangesAsync();

        await _acRepo.RemoveFromCollectionAsync(asset.Id, col1.Id);
        await _acRepo.RemoveFromCollectionAsync(asset.Id, col2.Id);

        // Asset still exists but has no collections
        var remaining = await _acRepo.GetCollectionIdsForAssetAsync(asset.Id);
        Assert.Empty(remaining);
        Assert.NotNull(await _assetRepo.GetByIdAsync(asset.Id));
    }

    // ── Collection deletion cascades AssetCollections ───────────────

    [Fact]
    public async Task CollectionDeletion_CascadesOnAssetCollections_ButNotAssets()
    {
        var collection = TestData.CreateCollection(name: "Doomed");
        var asset = TestData.CreateAsset(title: "Survivor");

        _db.Collections.Add(collection);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, collection.Id));
        await _db.SaveChangesAsync();

        await _collectionRepo.DeleteAsync(collection.Id);

        // The collection and AssetCollection join entries are gone
        Assert.Null(await _db.Collections.FindAsync(collection.Id));
        Assert.Empty(await _db.AssetCollections.Where(ac => ac.CollectionId == collection.Id).ToListAsync());

        // But the asset itself survives (orphaned)
        Assert.NotNull(await _assetRepo.GetByIdAsync(asset.Id));
    }

    [Fact]
    public async Task CollectionDeletion_AssetInOtherCollection_StillAccessible()
    {
        var colToDelete = TestData.CreateCollection(name: "Delete Me");
        var colToKeep = TestData.CreateCollection(name: "Keep Me");
        var asset = TestData.CreateAsset(title: "Multi-Homed");

        _db.Collections.AddRange(colToDelete, colToKeep);
        _db.Assets.Add(asset);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, colToDelete.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, colToKeep.Id));
        await _db.SaveChangesAsync();

        await _collectionRepo.DeleteAsync(colToDelete.Id);

        // Asset still reachable through the other collection
        var fromKept = await _assetRepo.GetByCollectionAsync(colToKeep.Id);
        Assert.Single(fromKept);
        Assert.Equal("Multi-Homed", fromKept[0].Title);
    }

    // ── Mixed roles — highest role wins ─────────────────────────────

    [Fact]
    public async Task MixedRoles_UserHasDifferentRolesInDifferentCollections()
    {
        var col1 = TestData.CreateCollection(name: "ViewOnly");
        var col2 = TestData.CreateCollection(name: "CanContribute");
        _db.Collections.AddRange(col1, col2);

        // User has viewer in col1, contributor in col2
        _db.CollectionAcls.Add(TestData.CreateAcl(col1.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(col2.Id, "user1", AclRole.Contributor));
        await _db.SaveChangesAsync();

        // Verify both ACLs exist
        var userAcls = (await _aclRepo.GetByUserAsync("user1")).ToList();
        Assert.Equal(2, userAcls.Count);

        // The highest role can be computed by the application's RoleHierarchy
        var roles = userAcls.Select(a => a.Role).ToList();
        Assert.Contains(AclRole.Viewer, roles);
        Assert.Contains(AclRole.Contributor, roles);
    }

    // ── Orphaned assets (0 collections) ─────────────────────────────

    [Fact]
    public async Task OrphanedAsset_NotVisibleInSearchAll_WhenFiltered()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);

        // This asset is in a collection
        var inCollection = TestData.CreateAsset(title: "Visible", status: AssetStatus.Ready);
        _db.Assets.Add(inCollection);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(inCollection.Id, collection.Id));

        // This asset is orphaned (no collection link)
        var orphaned = TestData.CreateAsset(title: "Orphan", status: AssetStatus.Ready);
        _db.Assets.Add(orphaned);
        await _db.SaveChangesAsync();

        // Search filtered to allowed collections — orphan is excluded
        var (results, total) = await _assetRepo.SearchAllAsync(
            allowedCollectionIds: new List<Guid> { collection.Id });

        Assert.Equal(1, total);
        Assert.Equal("Visible", results[0].Title);
    }

    [Fact]
    public async Task OrphanedAsset_StillAccessibleByDirectId()
    {
        var orphan = TestData.CreateAsset(title: "Orphan");
        _db.Assets.Add(orphan);
        await _db.SaveChangesAsync();

        // Direct ID lookup works (admin path)
        var found = await _assetRepo.GetByIdAsync(orphan.Id);
        Assert.NotNull(found);
        Assert.Equal("Orphan", found.Title);
    }

    // ── ACL cascade on collection deletion ──────────────────────────

    [Fact]
    public async Task CollectionDeletion_CascadesAclDeletion()
    {
        var collection = TestData.CreateCollection();
        _db.Collections.Add(collection);
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user1", AclRole.Viewer));
        _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, "user2", AclRole.Admin));
        await _db.SaveChangesAsync();

        await _collectionRepo.DeleteAsync(collection.Id);

        var remaining = await _db.CollectionAcls
            .Where(a => a.CollectionId == collection.Id)
            .ToListAsync();
        Assert.Empty(remaining);
    }

    // ── Hierarchical collection deletion ────────────────────────────

    [Fact]
    public async Task HierarchicalDeletion_CascadesDeep()
    {
        // Create a 3-level hierarchy
        var root = TestData.CreateCollection(name: "Root");
        _db.Collections.Add(root);
        await _db.SaveChangesAsync();

        var child = TestData.CreateCollection(name: "Child", parentId: root.Id);
        _db.Collections.Add(child);
        await _db.SaveChangesAsync();

        var grandchild = TestData.CreateCollection(name: "Grandchild", parentId: child.Id);
        _db.Collections.Add(grandchild);

        // Each level has an asset
        var asset1 = TestData.CreateAsset(title: "Root Asset");
        var asset2 = TestData.CreateAsset(title: "Child Asset");
        var asset3 = TestData.CreateAsset(title: "Grandchild Asset");
        _db.Assets.AddRange(asset1, asset2, asset3);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset1.Id, root.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset2.Id, child.Id));
        _db.AssetCollections.Add(TestData.CreateAssetCollection(asset3.Id, grandchild.Id));
        await _db.SaveChangesAsync();

        // Delete root — should cascade through child → grandchild
        await _collectionRepo.DeleteAsync(root.Id);

        // All 3 collections gone
        Assert.Null(await _db.Collections.FindAsync(root.Id));
        Assert.Null(await _db.Collections.FindAsync(child.Id));
        Assert.Null(await _db.Collections.FindAsync(grandchild.Id));

        // All AssetCollection links gone
        Assert.Empty(await _db.AssetCollections.ToListAsync());

        // But all 3 assets survive (orphaned)
        Assert.NotNull(await _db.Assets.FindAsync(asset1.Id));
        Assert.NotNull(await _db.Assets.FindAsync(asset2.Id));
        Assert.NotNull(await _db.Assets.FindAsync(asset3.Id));
    }
}
