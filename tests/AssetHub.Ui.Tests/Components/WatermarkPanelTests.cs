using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// bUnit tests for <see cref="WatermarkPanel"/> (T5-WMK-01 G-42).
/// Renders inside ManageAccessDialog; tested in isolation here.
/// </summary>
public class WatermarkPanelTests : BunitTestBase
{
    public WatermarkPanelTests()
    {
        Services.AddSingleton<IStringLocalizer<WatermarksResource>>(new StubStringLocalizer<WatermarksResource>());
    }

    [Fact]
    public void RendersTitleAndDescription()
    {
        var cut = Render<WatermarkPanel>(parameters => parameters
            .Add(p => p.CollectionId, Guid.NewGuid())
            .Add(p => p.Enabled, false)
            .Add(p => p.AssetCount, 0));

        Assert.Contains("Panel_Title", cut.Markup);
        Assert.Contains("Panel_Desc", cut.Markup);
        Assert.Contains("Toggle_Label", cut.Markup);
    }

    [Fact]
    public void ShowsOnHelper_WhenEnabledTrue()
    {
        var cut = Render<WatermarkPanel>(parameters => parameters
            .Add(p => p.CollectionId, Guid.NewGuid())
            .Add(p => p.Enabled, true)
            .Add(p => p.AssetCount, 5));

        Assert.Contains("Toggle_OnHelper", cut.Markup);
    }

    [Fact]
    public void ShowsOffHelper_WhenEnabledFalse()
    {
        var cut = Render<WatermarkPanel>(parameters => parameters
            .Add(p => p.CollectionId, Guid.NewGuid())
            .Add(p => p.Enabled, false)
            .Add(p => p.AssetCount, 0));

        Assert.Contains("Toggle_OffHelper", cut.Markup);
    }
}
