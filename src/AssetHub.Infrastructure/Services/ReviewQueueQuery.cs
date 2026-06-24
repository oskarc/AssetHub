using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Pending-review queue read model: assets currently in the <c>InReview</c> state, scoped to
/// the caller and annotated with area ownership (managers / "unassigned"). Split out of
/// <see cref="AssetReviewQueryService"/> so this read model stays within the type-coupling
/// budget. Ownership is a visibility annotation only — <see cref="IAssetWorkflowService"/>
/// remains the authorization source of truth.
/// </summary>
internal sealed class ReviewQueueQuery(
    DbContextProvider provider,
    ICollectionRepository collectionRepo,
    ICollectionAuthorizationService authService,
    IUserLookupService userLookup,
    CurrentUser currentUser,
    ILogger logger)
{
    // AclRole persists as a lowercase string, so an inequality (>=) would compare
    // alphabetically and wrongly drop "admin". Match the "area owner" roles explicitly.
    private static readonly AclRole[] ManagerRoles = [AclRole.Manager, AclRole.Admin];

    public async Task<ServiceResult<ReviewQueueResponse>> GetQueueAsync(ReviewQueueRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return ServiceError.Forbidden();

        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;

        var (isAdmin, managerScopeIds) = await ReviewQueryScope.ResolveManagerScopeAsync(
            collectionRepo, authService, currentUser, ct);

        // Base in-review set, scoped. The global query filter already excludes soft-deleted assets.
        var query = ApplyQueueFilters(db.Assets.AsNoTracking(), request, isAdmin, managerScopeIds);

        // The in-review set is a bounded, actively-worked queue, so we load the scoped
        // candidates, annotate ownership in memory (collections + ACLs can't be paged at
        // SQL level once joined), then filter/page. Each lookup below is batched — no N+1.
        var candidates = await query
            .OrderBy(a => a.WorkflowStateUpdatedAt ?? a.CreatedAt)
            .Select(a => new CandidateRow(
                a.Id, a.Title, a.AssetType, a.ThumbObjectKey, a.PosterObjectKey,
                a.WorkflowStateUpdatedAt ?? a.CreatedAt, a.CreatedByUserId))
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return new ReviewQueueResponse { Items = [], TotalCount = 0, UnassignedCount = 0 };

        var assetIds = candidates.Select(c => c.AssetId).ToList();

        var membership = await db.AssetCollections.AsNoTracking()
            .Where(ac => assetIds.Contains(ac.AssetId))
            .Select(ac => new { ac.AssetId, ac.CollectionId })
            .ToListAsync(ct);
        var collectionsByAsset = membership
            .GroupBy(m => m.AssetId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.CollectionId).Distinct().ToList());

        var involvedCollectionIds = membership.Select(m => m.CollectionId).Distinct().ToList();

        var areaNames = await db.Collections.AsNoTracking()
            .Where(c => involvedCollectionIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Direct manager-level ACLs per area. Inherited-ACL ownership (nested collections)
        // is not resolved here — a v1 simplification noted in the contract.
        var managerAcls = await db.CollectionAcls.AsNoTracking()
            .Where(acl => involvedCollectionIds.Contains(acl.CollectionId)
                          && acl.PrincipalType == PrincipalType.User
                          && ManagerRoles.Contains(acl.Role))
            .Select(acl => new { acl.CollectionId, acl.PrincipalId })
            .ToListAsync(ct);
        var managersByCollection = managerAcls
            .GroupBy(a => a.CollectionId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.PrincipalId).Distinct().ToList());

        var names = await ReviewQueryScope.ResolveNamesAsync(
            userLookup,
            managerAcls.Select(a => a.PrincipalId).Concat(candidates.Select(c => c.AuthorUserId)), ct);

        var items = candidates
            .Select(c => MapQueueItem(c, collectionsByAsset, areaNames, managersByCollection, names, isAdmin, managerScopeIds))
            .ToList();

        var unassignedCount = items.Count(i => i.IsUnassigned);
        var visible = request.UnassignedOnly ? items.Where(i => i.IsUnassigned).ToList() : items;
        var page = visible.Skip(request.Skip).Take(request.Take).ToList();

        logger.LogInformation(
            "Review queue for {UserId}: {Total} in-review ({Unassigned} unassigned), admin={IsAdmin}",
            currentUser.UserId, visible.Count, unassignedCount, isAdmin);

        return new ReviewQueueResponse
        {
            Items = page,
            TotalCount = visible.Count,
            UnassignedCount = unassignedCount
        };
    }

    /// <summary>Applies the scope + request filters to the in-review base set (kept separate so GetQueueAsync stays within the complexity budget).</summary>
    private static IQueryable<Asset> ApplyQueueFilters(
        IQueryable<Asset> query, ReviewQueueRequest request, bool isAdmin, List<Guid> managerScopeIds)
    {
        query = query.Where(a => a.WorkflowState == AssetWorkflowState.InReview);

        if (!isAdmin)
            query = query.Where(a => a.AssetCollections.Any(ac => managerScopeIds.Contains(ac.CollectionId)));

        if (!string.IsNullOrWhiteSpace(request.AssetType) && AssetEnumExtensions.IsValidAssetType(request.AssetType))
        {
            var type = request.AssetType.ToAssetType();
            query = query.Where(a => a.AssetType == type);
        }

        if (request.CollectionId is { } cid)
            query = query.Where(a => a.AssetCollections.Any(ac => ac.CollectionId == cid));

        return query;
    }

    /// <summary>Annotates one candidate with its areas, managers, unassigned flag, submitter, and can-act — all from the batched lookups.</summary>
    private static ReviewQueueItemDto MapQueueItem(
        CandidateRow c,
        IReadOnlyDictionary<Guid, List<Guid>> collectionsByAsset,
        IReadOnlyDictionary<Guid, string> areaNames,
        IReadOnlyDictionary<Guid, List<string>> managersByCollection,
        IReadOnlyDictionary<string, string> names,
        bool isAdmin,
        List<Guid> managerScopeIds)
    {
        var areaIds = collectionsByAsset.TryGetValue(c.AssetId, out var ids) ? ids : [];
        var managerIds = areaIds
            .SelectMany(id => managersByCollection.TryGetValue(id, out var m) ? m : [])
            .Distinct()
            .ToList();

        return new ReviewQueueItemDto
        {
            AssetId = c.AssetId,
            Title = c.Title,
            AssetType = c.AssetType.ToDbString(),
            ThumbObjectKey = c.ThumbObjectKey,
            PosterObjectKey = c.PosterObjectKey,
            Areas = areaIds
                .Select(id => new ReviewAreaDto
                {
                    Id = id,
                    Name = areaNames.TryGetValue(id, out var n) ? n : id.ToString()
                })
                .ToList(),
            Managers = managerIds.Select(id => names.TryGetValue(id, out var n) ? n : id).ToList(),
            IsUnassigned = managerIds.Count == 0,
            InReviewSince = c.InReviewSince,
            SubmittedBy = names.TryGetValue(c.AuthorUserId, out var an) ? an : c.AuthorUserId,
            CanAct = isAdmin || areaIds.Any(managerScopeIds.Contains)
        };
    }

    private sealed record CandidateRow(
        Guid AssetId, string Title, AssetType AssetType, string? ThumbObjectKey,
        string? PosterObjectKey, DateTime InReviewSince, string AuthorUserId);
}
