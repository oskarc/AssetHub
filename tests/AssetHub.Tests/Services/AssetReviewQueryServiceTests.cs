using AssetHub.Application;
using AssetHub.Application.Dtos;
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

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for <see cref="AssetReviewQueryService"/> against real Postgres.
/// The scoping/ownership-annotation logic is the meat: manager scope flows through the
/// real <see cref="CollectionAuthorizationService"/> + ACL rows, so the tests seed real
/// collections/ACLs/assets and assert visibility, the unassigned annotation, the can-act
/// flag, filters, pagination, soft-delete exclusion, and name resolution.
///
/// Only <see cref="IUserLookupService"/> is mocked — it's the incidental external (the
/// identity provider). Everything the service actually reasons over (collections, ACLs,
/// asset workflow state, transitions, the soft-delete query filter) is real.
/// </summary>
[Collection("Database")]
public class AssetReviewQueryServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private DbContextProvider _provider = null!;
    private CollectionRepository _collectionRepo = null!;

    // Identities used across the tests.
    private const string AdminId = "admin-001";
    private const string ManagerId = "manager-alice";
    private const string OtherManagerId = "manager-bob";
    private const string AuthorId = "author-carol";

    public AssetReviewQueryServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateMigratedDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        _provider = _fixture.CreateDbContextProvider(dbName);
        _collectionRepo = new CollectionRepository(
            _provider, TestCacheHelper.CreateHybridCache(), NullLogger<CollectionRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    // ── Service construction ─────────────────────────────────────────────

    /// <summary>
    /// Builds the service with the real DB-backed repo + auth service so the manager-scope
    /// resolution exercises actual ACL rows. <paramref name="names"/> seeds the mocked user
    /// lookup; any id not present resolves to itself (the production raw-id fallback).
    /// </summary>
    private AssetReviewQueryService CreateService(
        string userId,
        bool isAdmin,
        Dictionary<string, string>? names = null)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        var authService = new CollectionAuthorizationService(
            _provider, _collectionRepo, currentUser, NullLogger<CollectionAuthorizationService>.Instance);

        var userLookup = new Mock<IUserLookupService>();
        userLookup
            .Setup(s => s.GetUserNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
            {
                // Only return mappings the test asked for; unresolved ids are simply
                // absent so the service applies its raw-id fallback.
                var requested = ids.ToHashSet();
                return (names ?? new Dictionary<string, string>())
                    .Where(kv => requested.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            });

        return new AssetReviewQueryService(
            _provider, _collectionRepo, authService, userLookup.Object, currentUser,
            NullLogger<AssetReviewQueryService>.Instance);
    }

    // ── Seed helpers ─────────────────────────────────────────────────────

    /// <summary>Creates a collection and (optionally) grants the given principals roles on it.</summary>
    private async Task<Collection> SeedCollectionAsync(
        string name, params (string PrincipalId, AclRole Role)[] grants)
    {
        var collection = TestData.CreateCollection(name: $"{name}-{Guid.NewGuid():N}");
        _db.Collections.Add(collection);
        foreach (var (principalId, role) in grants)
            _db.CollectionAcls.Add(TestData.CreateAcl(collection.Id, principalId, role));
        await _db.SaveChangesAsync();
        return collection;
    }

    /// <summary>
    /// Inserts an in-review asset (the default workflow state for the queue) into the given
    /// collections. <paramref name="inReviewSince"/> drives the queue ordering.
    /// </summary>
    private async Task<Asset> SeedInReviewAssetAsync(
        string title,
        IEnumerable<Guid> collectionIds,
        AssetType assetType = AssetType.Image,
        string? authorId = null,
        DateTime? inReviewSince = null,
        bool softDeleted = false)
    {
        var asset = TestData.CreateAsset(title: title, assetType: assetType, createdByUserId: authorId ?? AuthorId);
        asset.WorkflowState = AssetWorkflowState.InReview;
        asset.WorkflowStateUpdatedAt = inReviewSince ?? DateTime.UtcNow;
        if (softDeleted)
        {
            asset.DeletedAt = DateTime.UtcNow;
            asset.DeletedByUserId = AdminId;
        }
        _db.Assets.Add(asset);
        foreach (var cid in collectionIds)
            _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, cid));
        await _db.SaveChangesAsync();
        return asset;
    }

    /// <summary>Inserts an asset in a given workflow state plus a transition row recording the decision.</summary>
    private async Task<(Asset Asset, AssetWorkflowTransition Transition)> SeedDecisionAsync(
        string title,
        IEnumerable<Guid> collectionIds,
        AssetWorkflowState toState,
        AssetWorkflowState fromState = AssetWorkflowState.InReview,
        string? actorId = null,
        string? reason = null,
        DateTime? decidedAt = null,
        bool softDeleted = false)
    {
        var asset = TestData.CreateAsset(title: title, createdByUserId: AuthorId);
        asset.WorkflowState = toState;
        if (softDeleted)
        {
            asset.DeletedAt = DateTime.UtcNow;
            asset.DeletedByUserId = AdminId;
        }
        _db.Assets.Add(asset);
        foreach (var cid in collectionIds)
            _db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, cid));

        var transition = TestData.CreateWorkflowTransition(
            assetId: asset.Id, fromState: fromState, toState: toState,
            actorUserId: actorId ?? ManagerId, reason: reason, createdAt: decidedAt ?? DateTime.UtcNow);
        _db.AssetWorkflowTransitions.Add(transition);
        await _db.SaveChangesAsync();
        return (asset, transition);
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetQueueAsync
    // ════════════════════════════════════════════════════════════════════

    // ── Authentication ───────────────────────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_Unauthenticated_ReturnsForbidden()
    {
        var svc = new AssetReviewQueryService(
            _provider, _collectionRepo,
            new CollectionAuthorizationService(_provider, _collectionRepo, CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance),
            Mock.Of<IUserLookupService>(), CurrentUser.Anonymous,
            NullLogger<AssetReviewQueryService>.Instance);

        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    // ── Scoping (admin vs manager) ───────────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_Admin_SeesAllInReviewAssetsAcrossCollections()
    {
        var c1 = await SeedCollectionAsync("c1", (ManagerId, AclRole.Manager));
        var c2 = await SeedCollectionAsync("c2", (OtherManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("InC1", new[] { c1.Id });
        await SeedInReviewAssetAsync("InC2", new[] { c2.Id });

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    [Fact]
    public async Task GetQueueAsync_Manager_OnlySeesAssetsInTheirManagerScopeCollections()
    {
        var mine = await SeedCollectionAsync("mine", (ManagerId, AclRole.Manager));
        var theirs = await SeedCollectionAsync("theirs", (OtherManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Mine", new[] { mine.Id });
        await SeedInReviewAssetAsync("Theirs", new[] { theirs.Id });

        var svc = CreateService(ManagerId, isAdmin: false);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Mine", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task GetQueueAsync_ViewerOnlyCollection_NotInManagerScope_SeesNothing()
    {
        // The caller is a viewer (not manager+) on the collection holding the asset.
        var col = await SeedCollectionAsync("viewer-only", (ManagerId, AclRole.Viewer));
        await SeedInReviewAssetAsync("Hidden", new[] { col.Id });

        var svc = CreateService(ManagerId, isAdmin: false);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalCount);
    }

    // ── Unassigned detection ─────────────────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_AssetInAreaWithNoManager_IsUnassigned()
    {
        // Admin sees it; the area has no manager-level ACL → unassigned.
        var orphanArea = await SeedCollectionAsync("orphan");
        await SeedInReviewAssetAsync("Orphaned", new[] { orphanArea.Id });

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.True(item.IsUnassigned);
        Assert.Empty(item.Managers);
        Assert.Equal(1, result.Value.UnassignedCount);
    }

    [Fact]
    public async Task GetQueueAsync_AssetInAreaWithManager_IsNotUnassigned_AndListsManagers()
    {
        var managed = await SeedCollectionAsync("managed", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Owned", new[] { managed.Id });

        var svc = CreateService(AdminId, isAdmin: true, names: new() { [ManagerId] = "Alice" });
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.False(item.IsUnassigned);
        Assert.Equal(0, result.Value.UnassignedCount);
        Assert.Contains("Alice", item.Managers);
    }

    [Fact]
    public async Task GetQueueAsync_UnassignedOnly_FiltersToOnlyUnassignedItems()
    {
        var managed = await SeedCollectionAsync("managed", (ManagerId, AclRole.Manager));
        var orphan = await SeedCollectionAsync("orphan");
        await SeedInReviewAssetAsync("Owned", new[] { managed.Id });
        await SeedInReviewAssetAsync("Orphaned", new[] { orphan.Id });

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(
            new ReviewQueueRequest { UnassignedOnly = true }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Orphaned", item.Title);
        Assert.Equal(1, result.Value.TotalCount);     // visible count is the unassigned subset
        Assert.Equal(1, result.Value.UnassignedCount); // count is computed before the UnassignedOnly filter
    }

    [Fact]
    public async Task GetQueueAsync_AssetWithManagerInAdminScopedRole_IsNotUnassigned()
    {
        // AclRole.Admin counts as an "area owner" (ManagerRoles = [Manager, Admin]).
        var col = await SeedCollectionAsync("admin-acl", (OtherManagerId, AclRole.Admin));
        await SeedInReviewAssetAsync("HasAreaAdmin", new[] { col.Id });

        var svc = CreateService(AdminId, isAdmin: true, names: new() { [OtherManagerId] = "Bob" });
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.False(item.IsUnassigned);
        Assert.Contains("Bob", item.Managers);
    }

    // ── CanAct ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_Admin_CanActOnEveryItem()
    {
        var orphan = await SeedCollectionAsync("orphan");
        await SeedInReviewAssetAsync("Orphaned", new[] { orphan.Id });

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value!.Items).CanAct);
    }

    [Fact]
    public async Task GetQueueAsync_ManagerInItsArea_CanActIsTrue()
    {
        var mine = await SeedCollectionAsync("mine", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Mine", new[] { mine.Id });

        var svc = CreateService(ManagerId, isAdmin: false);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value!.Items).CanAct);
    }

    // ── Area + manager name resolution ───────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_ResolvesAreaNames_ManagerNames_AndSubmittedBy()
    {
        var col = await SeedCollectionAsync("Marketing", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Doc", new[] { col.Id }, authorId: AuthorId);

        var svc = CreateService(AdminId, isAdmin: true,
            names: new() { [ManagerId] = "Alice", [AuthorId] = "Carol" });
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.StartsWith("Marketing", Assert.Single(item.Areas).Name);
        Assert.Contains("Alice", item.Managers);
        Assert.Equal("Carol", item.SubmittedBy);
    }

    [Fact]
    public async Task GetQueueAsync_UnresolvedUserIds_FallBackToRawId()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Doc", new[] { col.Id }, authorId: AuthorId);

        // No names seeded — both the manager and the author resolve to their raw ids.
        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Contains(ManagerId, item.Managers);
        Assert.Equal(AuthorId, item.SubmittedBy);
    }

    // ── Filters ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_AssetTypeFilter_NarrowsToThatType()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Photo", new[] { col.Id }, assetType: AssetType.Image);
        await SeedInReviewAssetAsync("Clip", new[] { col.Id }, assetType: AssetType.Video);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(
            new ReviewQueueRequest { AssetType = "video" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Clip", Assert.Single(result.Value!.Items).Title);
    }

    [Fact]
    public async Task GetQueueAsync_InvalidAssetTypeToken_IsIgnored()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Photo", new[] { col.Id }, assetType: AssetType.Image);
        await SeedInReviewAssetAsync("Clip", new[] { col.Id }, assetType: AssetType.Video);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(
            new ReviewQueueRequest { AssetType = "banana" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount); // unknown token = no filter applied
    }

    [Fact]
    public async Task GetQueueAsync_CollectionIdFilter_NarrowsToThatArea()
    {
        var c1 = await SeedCollectionAsync("c1", (ManagerId, AclRole.Manager));
        var c2 = await SeedCollectionAsync("c2", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("InC1", new[] { c1.Id });
        await SeedInReviewAssetAsync("InC2", new[] { c2.Id });

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(
            new ReviewQueueRequest { CollectionId = c1.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("InC1", Assert.Single(result.Value!.Items).Title);
    }

    // ── Pagination ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_SkipTake_PagesOverAnnotatedSet_TotalCountIsVisibleCount()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        // Distinct InReviewSince so the OrderBy is deterministic.
        var baseTime = DateTime.UtcNow.AddHours(-10);
        for (var i = 0; i < 5; i++)
            await SeedInReviewAssetAsync($"A{i:D2}", new[] { col.Id }, inReviewSince: baseTime.AddMinutes(i));

        var svc = CreateService(AdminId, isAdmin: true);
        var page = await svc.GetQueueAsync(
            new ReviewQueueRequest { Skip = 2, Take = 2 }, CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Equal(5, page.Value!.TotalCount); // total reflects all visible, not the page size
        Assert.Equal(2, page.Value.Items.Count);
        Assert.Equal("A02", page.Value.Items[0].Title); // oldest-first ordering, page 2
        Assert.Equal("A03", page.Value.Items[1].Title);
    }

    // ── Soft delete ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_SoftDeletedInReviewAsset_IsExcluded()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Live", new[] { col.Id });
        await SeedInReviewAssetAsync("Trashed", new[] { col.Id }, softDeleted: true);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Live", Assert.Single(result.Value!.Items).Title);
    }

    [Fact]
    public async Task GetQueueAsync_NonInReviewAssets_AreExcluded()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedInReviewAssetAsync("Pending", new[] { col.Id });
        // A Published (default) asset in the same area must not appear in the queue.
        var published = TestData.CreateAsset(title: "Published");
        _db.Assets.Add(published);
        _db.AssetCollections.Add(TestData.CreateAssetCollection(published.Id, col.Id));
        await _db.SaveChangesAsync();

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetQueueAsync(new ReviewQueueRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Pending", Assert.Single(result.Value!.Items).Title);
    }

    // ════════════════════════════════════════════════════════════════════
    //  GetDecisionsAsync
    // ════════════════════════════════════════════════════════════════════

    // ── Authentication ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDecisionsAsync_Unauthenticated_ReturnsForbidden()
    {
        var svc = new AssetReviewQueryService(
            _provider, _collectionRepo,
            new CollectionAuthorizationService(_provider, _collectionRepo, CurrentUser.Anonymous, NullLogger<CollectionAuthorizationService>.Instance),
            Mock.Of<IUserLookupService>(), CurrentUser.Anonymous,
            NullLogger<AssetReviewQueryService>.Instance);

        var result = await svc.GetDecisionsAsync(new ReviewDecisionsRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    // ── Decision-state surfacing ─────────────────────────────────────────

    [Fact]
    public async Task GetDecisionsAsync_OnlyDecisionStatesSurface_SubmissionsExcluded()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("Approved", new[] { col.Id }, AssetWorkflowState.Approved);
        await SeedDecisionAsync("Rejected", new[] { col.Id }, AssetWorkflowState.Rejected);
        await SeedDecisionAsync("Published", new[] { col.Id }, AssetWorkflowState.Published, fromState: AssetWorkflowState.Approved);
        // A submission (ToState = InReview) must NOT appear in the decisions feed.
        await SeedDecisionAsync("Submitted", new[] { col.Id }, AssetWorkflowState.InReview, fromState: AssetWorkflowState.Draft);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(new ReviewDecisionsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TotalCount);
        Assert.DoesNotContain(result.Value.Items, i => i.AssetTitle == "Submitted");
    }

    // ── Filters ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDecisionsAsync_DecisionTokenFilter_NarrowsToThatState()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("Approved", new[] { col.Id }, AssetWorkflowState.Approved);
        await SeedDecisionAsync("Rejected", new[] { col.Id }, AssetWorkflowState.Rejected);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(
            new ReviewDecisionsRequest { Decisions = new() { "rejected" } }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("rejected", item.Decision);
        Assert.Equal("Rejected", item.AssetTitle);
    }

    [Fact]
    public async Task GetDecisionsAsync_UnknownDecisionToken_IsIgnored_AndDoesNotThrow()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("Approved", new[] { col.Id }, AssetWorkflowState.Approved);
        await SeedDecisionAsync("Rejected", new[] { col.Id }, AssetWorkflowState.Rejected);

        // "unpublished" is documented historically but is not an enum member; "banana" is garbage.
        // Both must be ignored (never throw); the valid "approved" still narrows the result.
        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(
            new ReviewDecisionsRequest { Decisions = new() { "approved", "unpublished", "banana" } },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("approved", item.Decision);
        Assert.Equal("Approved", item.AssetTitle);
    }

    [Fact]
    public async Task GetDecisionsAsync_AllTokensUnknown_FallsBackToAllDecisions()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("Approved", new[] { col.Id }, AssetWorkflowState.Approved);
        await SeedDecisionAsync("Rejected", new[] { col.Id }, AssetWorkflowState.Rejected);

        // No valid token survives the guard → the request degrades to "all decisions".
        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(
            new ReviewDecisionsRequest { Decisions = new() { "banana", "unpublished" } },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    [Fact]
    public async Task GetDecisionsAsync_CollectionIdFilter_NarrowsToThatArea()
    {
        var c1 = await SeedCollectionAsync("c1", (ManagerId, AclRole.Manager));
        var c2 = await SeedCollectionAsync("c2", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("InC1", new[] { c1.Id }, AssetWorkflowState.Approved);
        await SeedDecisionAsync("InC2", new[] { c2.Id }, AssetWorkflowState.Approved);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(
            new ReviewDecisionsRequest { CollectionId = c1.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("InC1", Assert.Single(result.Value!.Items).AssetTitle);
    }

    [Fact]
    public async Task GetDecisionsAsync_AfterAndBeforeFilters_NarrowByDate()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        var old = DateTime.UtcNow.AddDays(-10);
        var mid = DateTime.UtcNow.AddDays(-5);
        var recent = DateTime.UtcNow.AddDays(-1);
        await SeedDecisionAsync("Old", new[] { col.Id }, AssetWorkflowState.Approved, decidedAt: old);
        await SeedDecisionAsync("Mid", new[] { col.Id }, AssetWorkflowState.Approved, decidedAt: mid);
        await SeedDecisionAsync("Recent", new[] { col.Id }, AssetWorkflowState.Approved, decidedAt: recent);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(
            new ReviewDecisionsRequest { After = old.AddDays(1), Before = recent.AddDays(-0.5) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Mid", Assert.Single(result.Value!.Items).AssetTitle);
    }

    // ── Ordering + pagination ────────────────────────────────────────────

    [Fact]
    public async Task GetDecisionsAsync_OrderedByDecidedAtDescending()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("First", new[] { col.Id }, AssetWorkflowState.Approved, decidedAt: DateTime.UtcNow.AddDays(-3));
        await SeedDecisionAsync("Second", new[] { col.Id }, AssetWorkflowState.Approved, decidedAt: DateTime.UtcNow.AddDays(-2));
        await SeedDecisionAsync("Third", new[] { col.Id }, AssetWorkflowState.Approved, decidedAt: DateTime.UtcNow.AddDays(-1));

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(new ReviewDecisionsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Third", result.Value!.Items[0].AssetTitle);
        Assert.Equal("Second", result.Value.Items[1].AssetTitle);
        Assert.Equal("First", result.Value.Items[2].AssetTitle);
    }

    [Fact]
    public async Task GetDecisionsAsync_SkipTake_TotalCountIsFullFilteredCount()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        for (var i = 0; i < 5; i++)
            await SeedDecisionAsync($"D{i}", new[] { col.Id }, AssetWorkflowState.Approved, decidedAt: DateTime.UtcNow.AddDays(-i));

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(
            new ReviewDecisionsRequest { Skip = 1, Take = 2 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.TotalCount); // full filtered count, independent of paging
        Assert.Equal(2, result.Value.Items.Count);
    }

    // ── Soft delete ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetDecisionsAsync_TransitionOfSoftDeletedAsset_IsExcluded()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("Live", new[] { col.Id }, AssetWorkflowState.Approved);
        await SeedDecisionAsync("Trashed", new[] { col.Id }, AssetWorkflowState.Approved, softDeleted: true);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(new ReviewDecisionsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Live", Assert.Single(result.Value!.Items).AssetTitle);
    }

    // ── Reviewer name resolution ─────────────────────────────────────────

    [Fact]
    public async Task GetDecisionsAsync_ReviewerName_ResolvesViaLookup()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("Doc", new[] { col.Id }, AssetWorkflowState.Approved, actorId: ManagerId);

        var svc = CreateService(AdminId, isAdmin: true, names: new() { [ManagerId] = "Alice" });
        var result = await svc.GetDecisionsAsync(new ReviewDecisionsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Alice", Assert.Single(result.Value!.Items).ReviewerName);
    }

    [Fact]
    public async Task GetDecisionsAsync_UnresolvedReviewerId_FallsBackToRawId()
    {
        var col = await SeedCollectionAsync("area", (ManagerId, AclRole.Manager));
        await SeedDecisionAsync("Doc", new[] { col.Id }, AssetWorkflowState.Approved, actorId: ManagerId);

        var svc = CreateService(AdminId, isAdmin: true); // no names seeded
        var result = await svc.GetDecisionsAsync(new ReviewDecisionsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ManagerId, Assert.Single(result.Value!.Items).ReviewerName);
    }

    // ── Scoping (admin vs manager) ───────────────────────────────────────

    [Fact]
    public async Task GetDecisionsAsync_Admin_SeesDecisionsAcrossAllCollections()
    {
        var c1 = await SeedCollectionAsync("c1", (ManagerId, AclRole.Manager));
        var c2 = await SeedCollectionAsync("c2", (OtherManagerId, AclRole.Manager));
        await SeedDecisionAsync("InC1", new[] { c1.Id }, AssetWorkflowState.Approved);
        await SeedDecisionAsync("InC2", new[] { c2.Id }, AssetWorkflowState.Approved);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.GetDecisionsAsync(new ReviewDecisionsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
    }

    [Fact]
    public async Task GetDecisionsAsync_Manager_OnlySeesDecisionsInTheirManagerScope()
    {
        var mine = await SeedCollectionAsync("mine", (ManagerId, AclRole.Manager));
        var theirs = await SeedCollectionAsync("theirs", (OtherManagerId, AclRole.Manager));
        await SeedDecisionAsync("Mine", new[] { mine.Id }, AssetWorkflowState.Approved);
        await SeedDecisionAsync("Theirs", new[] { theirs.Id }, AssetWorkflowState.Approved);

        var svc = CreateService(ManagerId, isAdmin: false);
        var result = await svc.GetDecisionsAsync(new ReviewDecisionsRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Mine", Assert.Single(result.Value.Items).AssetTitle);
    }
}
