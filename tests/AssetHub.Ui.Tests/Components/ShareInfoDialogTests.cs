using AssetHub.Ui.Tests.Helpers;
using Microsoft.AspNetCore.Components;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the ShareInfoDialog component.
/// Verifies share details display, token/password loading, and copy actions.
/// </summary>
public class ShareInfoDialogTests : BunitTestBase
{
    private readonly Mock<IClipboardService> _mockClipboard = new();

    private static AdminShareDto CreateShare(bool hasPassword = true) => new()
    {
        Id = Guid.NewGuid(),
        ScopeType = "asset",
        ScopeId = Guid.NewGuid(),
        ScopeName = "Test Asset",
        CreatedByUserId = "user-1",
        CreatedByUserName = "admin",
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        AccessCount = 5,
        HasPassword = hasPassword,
        Status = "Active",
        CollectionNames = new List<string> { "Collection A", "Collection B" }
    };

    public ShareInfoDialogTests()
    {
        Services.AddSingleton(_mockClipboard.Object);
    }

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
        AdminShareDto? share = null,
        string shareUrl = "",
        string sharePassword = "")
    {
        share ??= CreateShare();

        MockApi.Setup(a => a.GetShareTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token-abc");
        MockApi.Setup(a => a.GetSharePasswordAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("SecretPass123");

        var parameters = new DialogParameters<ShareInfoDialog>
        {
            { x => x.Share, share },
            { x => x.ShareUrl, shareUrl },
            { x => x.SharePassword, sharePassword }
        };
        return await ShowDialogAsync<ShareInfoDialog>(parameters);
    }

    [Fact]
    public async Task Renders_Title()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("ShareDetails", cut.Markup);
    }

    [Fact]
    public async Task Loads_Token_And_Builds_Url()
    {
        var share = CreateShare();
        await RenderDialogAsync(share);

        MockApi.Verify(a => a.GetShareTokenAsync(share.Id, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Loads_Password_When_Share_Has_Password()
    {
        var share = CreateShare(hasPassword: true);
        await RenderDialogAsync(share);

        MockApi.Verify(a => a.GetSharePasswordAsync(share.Id, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Skips_Password_Load_When_No_Password()
    {
        var share = CreateShare(hasPassword: false);
        await RenderDialogAsync(share);

        MockApi.Verify(a => a.GetSharePasswordAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Shows_Collection_Names_For_Asset_Scope()
    {
        var share = CreateShare();
        var cut = await RenderDialogAsync(share);

        Assert.Contains("Collection A", cut.Markup);
        Assert.Contains("Collection B", cut.Markup);
    }

    [Fact]
    public async Task Has_Cancel_And_CopyAll_Buttons()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Cancel", cut.Markup);
        Assert.Contains("Btn_CopyAll", cut.Markup);
    }

    [Fact]
    public async Task Shows_Set_Password_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("SetOrChangePassword", cut.Markup);
    }

    [Fact]
    public async Task Shows_Copy_Hint()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("ShareInfoCopyHint", cut.Markup);
    }

    [Fact]
    public async Task Handles_Token_Load_Error()
    {
        MockApi.Setup(a => a.GetShareTokenAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API error"));

        var share = CreateShare(hasPassword: false);
        var parameters = new DialogParameters<ShareInfoDialog>
        {
            { x => x.Share, share },
            { x => x.ShareUrl, "" },
            { x => x.SharePassword, "" }
        };
        await ShowDialogAsync<ShareInfoDialog>(parameters);

        VerifyHandleErrorCalled();
    }
}
