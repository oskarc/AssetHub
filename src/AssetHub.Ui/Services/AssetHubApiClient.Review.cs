using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<ReviewQueueResponse> GetReviewQueueAsync(ReviewQueueRequest request, CancellationToken ct = default)
    {
        Validate(request, "Get review queue");
        var result = await assetReviewQueryService.GetQueueAsync(request, ct);
        return Unwrap(result, "Get review queue");
    }

    public async Task<ReviewDecisionsResponse> GetReviewDecisionsAsync(ReviewDecisionsRequest request, CancellationToken ct = default)
    {
        Validate(request, "Get review decisions");
        var result = await assetReviewQueryService.GetDecisionsAsync(request, ct);
        return Unwrap(result, "Get review decisions");
    }
}
