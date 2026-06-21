using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Query-side surface for the Reviews section (manager+). Read-only:
/// the pending queue (assets in the <c>InReview</c> state, annotated with area
/// ownership and a per-caller can-act flag) and the decisions history (completed
/// workflow transitions).
///
/// Scoping: a manager sees assets in collections where they hold manager+;
/// an admin sees every in-review item, with ownerless areas flagged. This routing
/// decides queue <em>visibility</em> only — the transitions on
/// <see cref="IAssetWorkflowService"/> remain the authorization source of truth
/// (commands-vs-queries split). The decisions feed reads
/// <c>AssetWorkflowTransition</c> directly and is intentionally separate from the
/// generic audit log.
/// </summary>
public interface IAssetReviewQueryService
{
    /// <summary>Assets currently awaiting review, scoped and ownership-annotated for the caller.</summary>
    Task<ServiceResult<ReviewQueueResponse>> GetQueueAsync(ReviewQueueRequest request, CancellationToken ct);

    /// <summary>Completed review decisions (approve / reject / publish / unpublish), scoped to the caller.</summary>
    Task<ServiceResult<ReviewDecisionsResponse>> GetDecisionsAsync(ReviewDecisionsRequest request, CancellationToken ct);
}
