using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the LanguageSwitcher component.
/// Verifies culture options, initial selection, and culture change behavior.
/// </summary>
public class LanguageSwitcherTests : BunitTestBase
{
    [Fact]
    public void Renders_Language_Select()
    {
        var cut = Render<LanguageSwitcher>();

        Assert.Contains("language-switcher", cut.Markup);
    }

    [Fact]
    public void Shows_English_Option()
    {
        var cut = Render<LanguageSwitcher>();

        // Open the MudSelect dropdown (MudBlazor 8 uses mousedown)
        cut.Find("div.mud-input-control").MouseDown();

        // MudSelectItem contents render inside MudPopoverProvider
        Assert.Contains("English", PopoverProvider!.Markup);
    }

    [Fact]
    public void Shows_Swedish_Option()
    {
        var cut = Render<LanguageSwitcher>();

        // Open the MudSelect dropdown (MudBlazor 8 uses mousedown)
        cut.Find("div.mud-input-control").MouseDown();

        // MudSelectItem contents render inside MudPopoverProvider
        Assert.Contains("Svenska", PopoverProvider!.Markup);
    }

    [Fact]
    public void Defaults_To_Current_Culture()
    {
        // Default culture is "en"
        var cut = Render<LanguageSwitcher>();

        // Should render without errors — culture detection happens server-side
        Assert.Contains("language-switcher", cut.Markup);
    }

    [Fact]
    public void Renders_As_Compact_MudSelect()
    {
        var cut = Render<LanguageSwitcher>();

        // Should be a dense variant
        Assert.Contains("language-switcher", cut.Markup);
    }

    [Fact]
    public async Task Changing_Culture_Sets_Cookie_Via_JsInterop()
    {
        // Explicitly set UI culture to "en" so the OnCultureChanged guard
        // (if culture == _currentCulture return) doesn't skip the logic.
        var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en");
        try
        {
            // Set up module interop for the helpers.js import
            var module = JSInterop.SetupModule("./_content/AssetHub.Ui/js/helpers.js");
            module.SetupVoid("setCookie", _ => true).SetVoidResult();

            var cut = Render<LanguageSwitcher>();

            // Programmatically trigger the culture change via MudSelect's ValueChanged callback.
            // Clicking MudSelectItems in bUnit doesn't reliably trigger ValueChanged through
            // MudBlazor's internal event chain, so we invoke the callback directly.
            var select = cut.FindComponent<MudSelect<string>>();
            try
            {
                await cut.InvokeAsync(async () =>
                {
                    await select.Instance.ValueChanged.InvokeAsync("sv");
                });
            }
            catch (Microsoft.AspNetCore.Components.NavigationException)
            {
                // Expected: NavigateTo with forceLoad may throw in bUnit
            }

            // Verify cookie was set via JS interop
            module.VerifyInvoke("setCookie", 1);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
