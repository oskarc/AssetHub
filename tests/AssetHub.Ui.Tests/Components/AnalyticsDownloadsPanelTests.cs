using AssetHub.Ui.Components.Analytics;
using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// bUnit tests for the analytics downloads panel (T5-ANL-01).
/// </summary>
public class AnalyticsDownloadsPanelTests : BunitTestBase
{
    [Fact]
    public void RendersEmptyState_WhenNoData()
    {
        MockApi.Setup(a => a.GetTopDownloadedAssetsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AnalyticsAssetDownloadRowDto>());
        MockApi.Setup(a => a.GetDailyDownloadCountsAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AnalyticsDailyPointDto>());

        var cut = Render<AnalyticsDownloadsPanel>(parameters => parameters.Add(p => p.WindowDays,90));

        Assert.Contains("Empty_NoData", cut.Markup);
    }

    [Fact]
    public void RendersDeletedChip_ForSoftDeletedAsset()
    {
        // Forensic UX: a soft-deleted asset still surfaces in the analytics
        // table (so the admin can answer "this leaked asset was downloaded N
        // times before it was removed") but the row is non-clickable — the
        // /assets/{id} route would 404 for a deleted asset.
        MockApi.Setup(a => a.GetTopDownloadedAssetsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AnalyticsAssetDownloadRowDto
                {
                    AssetId = Guid.NewGuid(),
                    Title = "Removed-Asset",
                    AssetType = "image",
                    DownloadCount = 5,
                    IsDeleted = true
                }
            });
        MockApi.Setup(a => a.GetDailyDownloadCountsAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AnalyticsDailyPointDto>());

        var cut = Render<AnalyticsDownloadsPanel>(parameters => parameters.Add(p => p.WindowDays,90));

        Assert.Contains("Removed-Asset", cut.Markup);
        Assert.Contains("Downloads_Deleted", cut.Markup);
        // Deleted row must NOT have an /assets/{id} hyperlink.
        Assert.DoesNotContain("href=\"/assets/", cut.Markup);
    }

    [Fact]
    public void RendersAssetLink_ForLiveAsset()
    {
        var assetId = Guid.NewGuid();
        MockApi.Setup(a => a.GetTopDownloadedAssetsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AnalyticsAssetDownloadRowDto
                {
                    AssetId = assetId,
                    Title = "Live-Asset",
                    AssetType = "image",
                    DownloadCount = 12,
                    IsDeleted = false
                }
            });
        MockApi.Setup(a => a.GetDailyDownloadCountsAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AnalyticsDailyPointDto>());

        var cut = Render<AnalyticsDownloadsPanel>(parameters => parameters.Add(p => p.WindowDays,90));

        Assert.Contains("Live-Asset", cut.Markup);
        Assert.Contains($"/assets/{assetId}", cut.Markup);
        Assert.DoesNotContain("Downloads_Deleted", cut.Markup);
    }
}
