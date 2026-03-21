using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the CreateShareDialog component.
/// Verifies password generation, expiration selection, email management, and share creation.
/// </summary>
public class CreateShareDialogTests : BunitTestBase
{
    private readonly Guid _scopeId = Guid.NewGuid();

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
        string scopeType = "asset",
        string contentName = "My Asset")
    {
        var parameters = new DialogParameters<CreateShareDialog>
        {
            { x => x.ScopeId, _scopeId },
            { x => x.ScopeType, scopeType },
            { x => x.ContentName, contentName }
        };
        return await ShowDialogAsync<CreateShareDialog>(parameters);
    }

    [Fact]
    public async Task Renders_Dialog_With_Share_Title()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("CreateShareLink", cut.Markup);
    }

    [Fact]
    public async Task Shows_Content_Name_In_Description()
    {
        var cut = await RenderDialogAsync(contentName: "Important Document");

        // Stub localizer returns the key as-is; ContentName appears via string.Format
        Assert.Contains("CreateShareDesc", cut.Markup);
    }

    [Fact]
    public async Task Generates_Password_On_Init()
    {
        var cut = await RenderDialogAsync();

        // Password field should have a value (auto-generated)
        var passwordInput = cut.FindAll("input").FirstOrDefault(i =>
            i.GetAttribute("type") == "text" || i.GetAttribute("type") == "password");
        Assert.NotNull(passwordInput);
        var value = passwordInput.GetAttribute("value");
        // Generated password should be non-empty (12 chars)
        Assert.False(string.IsNullOrEmpty(value));
    }

    [Fact]
    public async Task Has_Generate_New_Password_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_GenerateNew", cut.Markup);
    }

    [Fact]
    public async Task Has_Password_Visibility_Toggle()
    {
        var cut = await RenderDialogAsync();

        // Should have adornment icon button for visibility toggle
        var adornmentButtons = cut.FindAll(".mud-input-adornment-end");
        Assert.NotEmpty(adornmentButtons);
    }

    [Fact]
    public async Task Has_Expiration_Selector()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("ExpiresIn", cut.Markup);
    }

    [Fact]
    public async Task Has_Email_Notification_Section()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("NotifyEmail", cut.Markup);
        Assert.Contains("AddEmailLabel", cut.Markup);
    }

    [Fact]
    public async Task Has_Cancel_And_Create_Buttons()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Cancel", cut.Markup);
        Assert.Contains("CreateShare", cut.Markup);
    }

    [Fact]
    public async Task Renders_Share_Icon()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains(Icons.Material.Filled.Share, cut.Markup);
    }

    [Fact]
    public async Task Asset_Scope_Type_Renders_Correctly()
    {
        var cut = await RenderDialogAsync(scopeType: "asset");

        // Should render without errors
        Assert.Contains("CreateShareLink", cut.Markup);
    }

    [Fact]
    public async Task Collection_Scope_Type_Renders_Correctly()
    {
        var cut = await RenderDialogAsync(scopeType: "collection");

        // Should render without errors
        Assert.Contains("CreateShareLink", cut.Markup);
    }

    [Fact]
    public async Task Expiration_Option_Values_Present()
    {
        var cut = await RenderDialogAsync();

        // Open the expiration MudSelect dropdown (MudBlazor 8 uses mousedown)
        await cut.Find("div.mud-select div.mud-input-control").MouseDownAsync();

        // MudSelectItem contents now render inside MudPopoverProvider
        Assert.Contains("Expiry_7Days", PopoverProvider!.Markup);
    }

    // ── Create Share submission flow ────────────────────────────────

    [Fact]
    public async Task CreateShare_Calls_CreateShareAsync()
    {
        var shareResponse = TestData.CreateShareResponse(scopeId: _scopeId);
        MockApi.Setup(a => a.CreateShareAsync(
                _scopeId,
                "asset",
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(shareResponse);

        var cut = await RenderDialogAsync();

        // Click the Create Share button
        var createBtn = cut.FindAll("button")
            .First(b => b.TextContent.Contains("CreateShare"));
        await cut.InvokeAsync(() => createBtn.Click());

        MockApi.Verify(a => a.CreateShareAsync(
            _scopeId,
            "asset",
            It.IsAny<DateTime?>(),
            It.IsAny<string?>(),
            It.IsAny<List<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task CreateShare_Error_Calls_HandleError()
    {
        MockApi.Setup(a => a.CreateShareAsync(
                _scopeId,
                "asset",
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        var cut = await RenderDialogAsync();

        var createBtn = cut.FindAll("button")
            .First(b => b.TextContent.Contains("CreateShare"));
        await cut.InvokeAsync(() => createBtn.Click());

        VerifyHandleErrorCalled();
    }
}
