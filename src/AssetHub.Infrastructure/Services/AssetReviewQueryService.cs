using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Query side of the Reviews section (T-REVIEW). Read-only: the pending queue and the
/// decisions history. Scoping mirrors <see cref="AssetSearchService"/> — admins see
/// everything; a manager is scoped to collections where they hold manager+. Ownership
/// (area managers / "unassigned") is a <em>visibility annotation</em>, not an authorization
/// change — the transitions on <see cref="IAssetWorkflowService"/> remain the source of
/// truth for who may act.
///
/// This type is a thin façade over two single-read-model collaborators
/// (<see cref="ReviewQueueQuery"/>, <see cref="ReviewDecisionsQuery"/>): the queue and the
/// decisions history are independent read models, so each lives in its own class to stay
/// within the type-coupling budget while presenting one interface to callers. Shared scoping
/// lives in <see cref="ReviewQueryScope"/>.
/// </summary>
public sealed class AssetReviewQueryService(
    DbContextProvider provider,
    ICollectionRepository collectionRepo,
    ICollectionAuthorizationService authService,
    IUserLookupService userLookup,
    CurrentUser currentUser,
    ILogger<AssetReviewQueryService> logger) : IAssetReviewQueryService
{
    private readonly ReviewQueueQuery _queue =
        new(provider, collectionRepo, authService, userLookup, currentUser, logger);

    private readonly ReviewDecisionsQuery _decisions =
        new(provider, collectionRepo, authService, userLookup, currentUser);

    public Task<ServiceResult<ReviewQueueResponse>> GetQueueAsync(ReviewQueueRequest request, CancellationToken ct) =>
        _queue.GetQueueAsync(request, ct);

    public Task<ServiceResult<ReviewDecisionsResponse>> GetDecisionsAsync(ReviewDecisionsRequest request, CancellationToken ct) =>
        _decisions.GetDecisionsAsync(request, ct);
}
