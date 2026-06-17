using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<IReadOnlyList<AnalyticsAssetDownloadRowDto>> GetTopDownloadedAssetsAsync(
        int windowDays, int take, CancellationToken ct = default)
    {
        var result = await analyticsService.GetTopDownloadedAssetsAsync(windowDays, take, ct);
        return Unwrap(result, "Get top downloaded assets");
    }

    public async Task<IReadOnlyList<AnalyticsDailyPointDto>> GetDailyDownloadCountsAsync(
        int windowDays, CancellationToken ct = default)
    {
        var result = await analyticsService.GetDailyDownloadCountsAsync(windowDays, ct);
        return Unwrap(result, "Get daily download counts");
    }

    public async Task<IReadOnlyList<AnalyticsStorageByCollectionRowDto>> GetStorageByCollectionAsync(
        int take, CancellationToken ct = default)
    {
        var result = await analyticsService.GetStorageByCollectionAsync(take, ct);
        return Unwrap(result, "Get storage by collection");
    }

    public async Task<IReadOnlyList<AnalyticsStorageByAssetTypeRowDto>> GetStorageByAssetTypeAsync(
        CancellationToken ct = default)
    {
        var result = await analyticsService.GetStorageByAssetTypeAsync(ct);
        return Unwrap(result, "Get storage by asset type");
    }

    public async Task<IReadOnlyList<AnalyticsExposureRowDto>> GetTopRecipientsAsync(
        int windowDays, int take, CancellationToken ct = default)
    {
        var result = await analyticsService.GetTopRecipientsAsync(windowDays, take, ct);
        return Unwrap(result, "Get top recipients");
    }

    public async Task<RevealRecipientResponseDto> RevealRecipientAsync(
        RevealRecipientRequestDto request, CancellationToken ct = default)
    {
        Validate(request, "Reveal recipient");
        var result = await analyticsService.RevealRecipientAsync(request, ct);
        return Unwrap(result, "Reveal recipient");
    }

    public async Task<AnalyticsPdfJobStatusDto> EnqueueAnalyticsPdfAsync(
        int windowDays, CancellationToken ct = default)
    {
        var result = await analyticsPdfJobService.EnqueueAsync(windowDays, ct);
        return Unwrap(result, "Enqueue analytics PDF");
    }

    public async Task<AnalyticsPdfJobStatusDto> GetAnalyticsPdfStatusAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var result = await analyticsPdfJobService.GetStatusAsync(jobId, ct);
        return Unwrap(result, "Get analytics PDF status");
    }
}
