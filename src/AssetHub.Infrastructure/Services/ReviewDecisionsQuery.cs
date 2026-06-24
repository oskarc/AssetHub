using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Decisions/history read model: completed review outcomes (approve / reject / publish),
/// sourced from <c>AssetWorkflowTransition</c> and scoped like the queue. Split out of
/// <see cref="AssetReviewQueryService"/> so this read model stays within the type-coupling
/// budget. Intentionally independent of the generic audit log.
/// </summary>
internal sealed class ReviewDecisionsQuery(
    DbContextProvider provider,
    ICollectionRepository collectionRepo,
    ICollectionAuthorizationService authService,
    IUserLookupService userLookup,
    CurrentUser currentUser)
{
    // Decision transitions surfaced by the history view. Submissions (ToState = InReview)
    // are excluded; an unpublish lands on Approved and so groups under "approved" in v1.
    private static readonly AssetWorkflowState[] DecisionStates =
        [AssetWorkflowState.Approved, AssetWorkflowState.Rejected, AssetWorkflowState.Published];

    public async Task<ServiceResult<ReviewDecisionsResponse>> GetDecisionsAsync(ReviewDecisionsRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return ServiceError.Forbidden();

        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;

        var (isAdmin, managerScopeIds) = await ReviewQueryScope.ResolveManagerScopeAsync(
            collectionRepo, authService, currentUser, ct);

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

        var names = await ReviewQueryScope.ResolveNamesAsync(userLookup, rows.Select(r => r.ActorUserId), ct);

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

    private static List<AssetWorkflowState> ResolveDecisionStates(List<string>? tokens)
    {
        if (tokens is not { Count: > 0 })
            return [.. DecisionStates];

        // Guard with IsValidAssetWorkflowState before parsing: ToAssetWorkflowState throws on an
        // unknown token and the DTO doesn't validate token values, so a stray token (or the
        // historically documented-but-non-member "unpublished") would otherwise surface as a 500.
        // Mirror the queue's IsValidAssetType guard — unknown tokens are silently ignored.
        var parsed = tokens
            .Where(WorkflowEnumExtensions.IsValidAssetWorkflowState)
            .Select(t => t.ToAssetWorkflowState())
            .Where(DecisionStates.Contains)
            .Distinct()
            .ToList();
        return parsed.Count == 0 ? [.. DecisionStates] : parsed;
    }

    private sealed record DecisionRow(
        Guid Id, Guid AssetId, string Title, AssetWorkflowState ToState,
        string ActorUserId, string? Reason, DateTime CreatedAt);
}
