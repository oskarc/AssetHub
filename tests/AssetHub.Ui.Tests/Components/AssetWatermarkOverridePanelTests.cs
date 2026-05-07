using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// bUnit tests for <see cref="AssetWatermarkOverridePanel"/> (T5-WMK-01 G-42).
/// </summary>
public class AssetWatermarkOverridePanelTests : BunitTestBase
{
    public AssetWatermarkOverridePanelTests()
    {
        Services.AddSingleton<IStringLocalizer<WatermarksResource>>(new StubStringLocalizer<WatermarksResource>());
    }

    [Fact]
    public void RendersEffectiveOnBadge_WhenEffectiveOnTrue()
    {
        var cut = Render<AssetWatermarkOverridePanel>(parameters => parameters
            .Add(p => p.AssetId, Guid.NewGuid())
            .Add(p => p.EffectiveOn, true)
            .Add(p => p.CanEdit, true));

        Assert.Contains("Override_EffectiveOn", cut.Markup);
    }

    [Fact]
    public void RendersEffectiveOffBadge_WhenEffectiveOnFalse()
    {
        var cut = Render<AssetWatermarkOverridePanel>(parameters => parameters
            .Add(p => p.AssetId, Guid.NewGuid())
            .Add(p => p.EffectiveOn, false)
            .Add(p => p.CanEdit, true));

        Assert.Contains("Override_EffectiveOff", cut.Markup);
    }

    [Fact]
    public void ShowsPendingFingerprintNotice_WhenEffectiveOnButFingerprintNotApplied()
    {
        // No-gap rule visualisation: effective-on but the asset-fingerprint hasn't been
        // baked into stored renditions yet → the user is told downloads embed both layers
        // on the fly until the next sweep. This is the on-screen expression of the rule.
        var cut = Render<AssetWatermarkOverridePanel>(parameters => parameters
            .Add(p => p.AssetId, Guid.NewGuid())
            .Add(p => p.EffectiveOn, true)
            .Add(p => p.AssetWatermarkAppliedAt, (DateTime?)null)
            .Add(p => p.CanEdit, true));

        Assert.Contains("Override_FingerprintPending", cut.Markup);
    }

    [Fact]
    public void DoesNotShowPendingNotice_WhenFingerprintApplied()
    {
        var cut = Render<AssetWatermarkOverridePanel>(parameters => parameters
            .Add(p => p.AssetId, Guid.NewGuid())
            .Add(p => p.EffectiveOn, true)
            .Add(p => p.AssetWatermarkAppliedAt, DateTime.UtcNow.AddHours(-1))
            .Add(p => p.CanEdit, true));

        Assert.DoesNotContain("Override_FingerprintPending", cut.Markup);
        Assert.Contains("Override_FingerprintApplied", cut.Markup);
    }

    [Fact]
    public void RendersReadOnlyLabel_WhenCannotEdit()
    {
        // Viewers see the effective state but no editable control — same data, different
        // affordance. The mud-select shouldn't be in the markup.
        var cut = Render<AssetWatermarkOverridePanel>(parameters => parameters
            .Add(p => p.AssetId, Guid.NewGuid())
            .Add(p => p.EffectiveOn, true)
            .Add(p => p.CanEdit, false));

        Assert.DoesNotContain("mud-select", cut.Markup);
    }

    [Fact]
    public void RendersAllThreeOptions_WhenEditable()
    {
        var cut = Render<AssetWatermarkOverridePanel>(parameters => parameters
            .Add(p => p.AssetId, Guid.NewGuid())
            .Add(p => p.CanEdit, true));

        // The select itself renders inline; option items render in the popover provider.
        Assert.Contains("mud-select", cut.Markup);
    }
}
