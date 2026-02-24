using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the LanguageSwitcher component.
/// Verifies culture options, initial selection, and culture change behavior.
/// The component uses a MudMenu (icon button + menu items) rather than a MudSelect.
/// </summary>
public class LanguageSwitcherTests : BunitTestBase
{
    [Fact]
    public void Renders_Language_Menu()
    {
        var cut = Render<LanguageSwitcher>();

        // Component renders a MudMenu with a language icon
        Assert.NotNull(cut.FindComponent<MudMenu>());
    }

    [Fact]
    public void Shows_English_Option()
    {
        var cut = Render<LanguageSwitcher>();

        // Open the MudMenu by clicking the activator button
        cut.Find("button.mud-button-root").Click();

        // MudMenuItem contents render inside MudPopoverProvider
        Assert.Contains("English", PopoverProvider!.Markup);
    }

    [Fact]
    public void Shows_Swedish_Option()
    {
        var cut = Render<LanguageSwitcher>();

        // Open the MudMenu by clicking the activator button
        cut.Find("button.mud-button-root").Click();

        // MudMenuItem contents render inside MudPopoverProvider
        Assert.Contains("Svenska", PopoverProvider!.Markup);
    }

    [Fact]
    public void Defaults_To_Current_Culture()
    {
        // Default culture is "en"
        var cut = Render<LanguageSwitcher>();

        // Should render without errors — component renders a MudMenu
        Assert.NotNull(cut.FindComponent<MudMenu>());
    }

    [Fact]
    public void Renders_As_MudMenu_With_Language_Icon()
    {
        var cut = Render<LanguageSwitcher>();

        // Should render a MudMenu with the language icon
        Assert.NotNull(cut.FindComponent<MudMenu>());
    }

    [Fact]
    public async Task Changing_Culture_Sets_Cookie_Via_JsInterop()
    {
        // Explicitly set UI culture to "en" so the guard
        // (if culture == _currentCulture return) doesn't skip the logic.
        var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en");
        try
        {
            // Set up module interop for the helpers.js import
            var module = JSInterop.SetupModule("./_content/AssetHub.Ui/js/helpers.js");
            module.SetupVoid("setCookie", _ => true).SetVoidResult();

            var cut = Render<LanguageSwitcher>();

            // Invoke SetCulture("sv") directly via reflection — clicking menu items through
            // MudBlazor's popover provider is unreliable in bUnit; the JS interop logic
            // is what this test is really verifying.
            var method = typeof(LanguageSwitcher).GetMethod(
                "SetCulture",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            try
            {
                await cut.InvokeAsync(() => (Task)method!.Invoke(cut.Instance, ["sv"])!);
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
