using AssetHub.Ui.Components.Analytics;
using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// bUnit tests for the analytics exposure panel (T5-ANL-01). Focused on
/// the panel's two distinct UI states (empty vs populated) and the
/// reveal-by-click flow that guards admin-side PII access.
/// </summary>
public class AnalyticsExposurePanelTests : BunitTestBase
{
    [Fact]
    public void RendersEmptyState_WhenNoRecipients()
    {
        MockApi.Setup(a => a.GetTopRecipientsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AnalyticsExposureRowDto>());

        var cut = Render<AnalyticsExposurePanel>(parameters => parameters.Add(p => p.WindowDays,90));

        Assert.Contains("Empty_NoData", cut.Markup);
    }

    [Fact]
    public void RendersHashedRecipient_AndRevealButton_BeforeClick()
    {
        var hash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789==";
        MockApi.Setup(a => a.GetTopRecipientsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AnalyticsExposureRowDto
                {
                    RecipientHash = hash,
                    RecipientKind = "user",
                    DownloadCount = 5,
                    DistinctAssetCount = 3
                }
            });

        var cut = Render<AnalyticsExposurePanel>(parameters => parameters.Add(p => p.WindowDays,90));

        // The hash should be truncated for readability — first 24 chars + ellipsis.
        Assert.Contains("abcdef0123456789abcdef01", cut.Markup);
        // The full hash MUST NOT be displayed in the table (forensic UX); only
        // 24 chars + an ellipsis. (Spot-check by ensuring the trailing == is absent.)
        Assert.DoesNotContain(hash, cut.Markup);
        // Reveal button visible before any click.
        Assert.Contains("Btn_Reveal", cut.Markup);
    }

    [Fact]
    public void RendersDownloadCounts()
    {
        MockApi.Setup(a => a.GetTopRecipientsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AnalyticsExposureRowDto
                {
                    RecipientHash = new string('a', 64),
                    RecipientKind = "email",
                    DownloadCount = 42,
                    DistinctAssetCount = 7
                }
            });

        var cut = Render<AnalyticsExposurePanel>(parameters => parameters.Add(p => p.WindowDays,30));

        Assert.Contains("42", cut.Markup);
        Assert.Contains("7", cut.Markup);
        Assert.Contains("Exposure_Kind_Email", cut.Markup);
    }
}
