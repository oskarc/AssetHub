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
/// Query side of the Reviews section (T-REVIEW). Read-only: the pending queue and
/// the decisions history. Scoping mirrors <see cref="AssetSearchService"/> — admins
/// see everything; a manager is scoped to collections where they hold manager+.
/// Ownership (area managers / "unassigned") is a <em>visibility annotation</em>, not
/// an authorization change — the transitions on <see cref="IAssetWorkflowService"/>
/// remain the source of truth for who may act.
/// </summary>
public sealed class AssetReviewQueryService(
    DbContextProvider provider,
    ICollectionRepository collectionRepo,
    ICollectionAuthorizationService authService,
    IUserLookupService userLookup,
    CurrentUser currentUser,
    ILogger<AssetReviewQueryService> logger) : IAssetReviewQueryService
{
    // Decision transitions surfaced by the history view. Submissions (ToState = InReview)
    // are excluded; an unpublish lands on Approved and so groups under "approved" in v1.
    private static readonly AssetWorkflowState[] DecisionStates =
        [AssetWorkflowState.Approved, AssetWorkflowState.Rejected, AssetWorkflowState.Published];

    // AclRole persists as a lowercase string, so an inequality (>=) would compare
    // alphabetically and wrongly drop "admin". Match the "area owner" roles explicitly.
    private static readonly AclRole[] ManagerRoles = [AclRole.Manager, AclRole.Admin];

    public async Task<ServiceResult<ReviewQueueResponse>> GetQueueAsync(ReviewQueueRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return ServiceError.Forbidden();

        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;

        var (isAdmin, managerScopeIds) = await ResolveManagerScopeAsync(ct);

        // Base in-review set, scoped. The global query filter already excludes soft-deleted assets.
        var query = db.Assets.AsNoTracking()
            .Where(a => a.WorkflowState == AssetWorkflowState.InReview);

        if (!isAdmin)
            query = query.Where(a => a.AssetCollections.Any(ac => managerScopeIds.Contains(ac.CollectionId)));

        if (!string.IsNullOrWhiteSpace(request.AssetType) && AssetEnumExtensions.IsValidAssetType(request.AssetType))
        {
            var type = request.AssetType.ToAssetType();
            query = query.Where(a => a.AssetType == type);
        }

        if (request.CollectionId is { } cid)
            query = query.Where(a => a.AssetCollections.Any(ac => ac.CollectionId == cid));

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

        var names = await ResolveNamesAsync(
            managerAcls.Select(a => a.PrincipalId).Concat(candidates.Select(c => c.AuthorUserId)), ct);

        var items = candidates.Select(c =>
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
        }).ToList();

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

    public async Task<ServiceResult<ReviewDecisionsResponse>> GetDecisionsAsync(ReviewDecisionsRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return ServiceError.Forbidden();

        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;

        var (isAdmin, managerScopeIds) = await ResolveManagerScopeAsync(ct);

        var states = ResolveDecisionStates(request.Decisions);

        var query = db.AssetWorkflowTransitions.AsNoTracking()
            .Where(t => states.Contains(t.ToState))
            // EF query-expression null check (sanctioned) — skip transitions whose asset is trashed.
            .Where(t => t.Asset!.DeletedAt == null);

        if (!isAdmin)
            query = query.Where(t => t.Asset!.AssetCollections.Any(ac => managerScopeIds.Contains(ac.CollectionId)));

        if (request.CollectionId is { } cid)
            query = query.Where(t => t.Asset!.AssetCollections.Any(ac => ac.CollectionId == cid));
        if (request.After is { } after)
            query = query.Where(t => t.CreatedAt >= after);
        if (request.Before is { } before)
            query = query.Where(t => t.CreatedAt <= before);

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(t => new DecisionRow(t.Id, t.AssetId, t.Asset!.Title, t.ToState, t.ActorUserId, t.Reason, t.CreatedAt))
            .ToListAsync(ct);

        var names = await ResolveNamesAsync(rows.Select(r => r.ActorUserId), ct);

        var items = rows.Select(r => new ReviewDecisionDto
        {
            TransitionId = r.Id,
            AssetId = r.AssetId,
            AssetTitle = r.Title,
            Decision = r.ToState.ToDbString(),
            ReviewerName = names.TryGetValue(r.ActorUserId, out var n) ? n : r.ActorUserId,
            Reason = r.Reason,
            DecidedAt = r.CreatedAt
        }).ToList();

        return new ReviewDecisionsResponse { Items = items, TotalCount = total };
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the caller's review scope. System admins see everything (the second
    /// tuple element is unused). Everyone else is scoped to the collections where they
    /// hold manager+, computed over only their accessible collections to keep it cheap.
    /// </summary>
    private async Task<(bool IsAdmin, List<Guid> ManagerCollectionIds)> ResolveManagerScopeAsync(CancellationToken ct)
    {
        if (currentUser.IsSystemAdmin)
            return (true, []);

        var accessible = await collectionRepo.GetAccessibleCollectionsAsync(currentUser.UserId, ct);
        var accessibleIds = accessible.Select(c => c.Id).ToList();
        if (accessibleIds.Count == 0)
            return (false, []);

        var managerIds = await authService.FilterAccessibleAsync(
            currentUser.UserId, accessibleIds, RoleHierarchy.Roles.Manager, ct);
        return (false, managerIds);
    }

    private static List<AssetWorkflowState> ResolveDecisionStates(List<string>? tokens)
    {
        if (tokens is not { Count: > 0 })
            return [.. DecisionStates];

        var parsed = tokens
            .Select(t => t.ToAssetWorkflowState())
            .Where(DecisionStates.Contains)
            .Distinct()
            .ToList();
        return parsed.Count == 0 ? [.. DecisionStates] : parsed;
    }

    private async Task<Dictionary<string, string>> ResolveNamesAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var distinct = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        return distinct.Count == 0
            ? new Dictionary<string, string>()
            : await userLookup.GetUserNamesAsync(distinct, ct);
    }

    private sealed record CandidateRow(
        Guid AssetId, string Title, AssetType AssetType, string? ThumbObjectKey,
        string? PosterObjectKey, DateTime InReviewSince, string AuthorUserId);

    private sealed record DecisionRow(
        Guid Id, Guid AssetId, string Title, AssetWorkflowState ToState,
        string ActorUserId, string? Reason, DateTime CreatedAt);
}
