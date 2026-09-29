using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for AccessDeniedContent — the body shared by the /access-denied page and
/// the router's NotAuthorized branch (the in-circuit path is covered by E2E).
/// </summary>
public class AccessDeniedContentTests : BunitTestBase
{
    public AccessDeniedContentTests()
    {
        // In the constructor, not the test: InitializeAsync renders the popover
        // provider, after which bUnit refuses new service registrations.
        Services.AddSingleton<IStringLocalizer<CommonResource>>(new ArgumentEchoLocalizer());
        AddAuthorization().SetAuthorized("Test Viewer");
    }

    [Fact]
    public void AccessDeniedContent_WhenSignedIn_RendersOneHeadingTheNameAndBothLinks()
    {
        var cut = Render<AccessDeniedContent>();

        var heading = Assert.Single(cut.FindAll("h1"));
        Assert.Equal("AccessDenied_Title", heading.TextContent.Trim());
        Assert.Contains("AccessDenied_SignedInAs(Test Viewer)", cut.Markup);
        Assert.NotNull(cut.Find("a[href='/']"));
        Assert.NotNull(cut.Find("a[href='/auth/logout']"));
        Assert.All(cut.FindAll("svg"), svg => Assert.Equal("true", svg.GetAttribute("aria-hidden")));
    }

    /// <summary>
    /// The shared stub drops format arguments; this one keeps them, so the test can
    /// see which name was passed.
    /// </summary>
    private sealed class ArgumentEchoLocalizer : IStringLocalizer<CommonResource>
    {
        public LocalizedString this[string name] => new(name, name, false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, $"{name}({string.Join(", ", arguments)})", false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
