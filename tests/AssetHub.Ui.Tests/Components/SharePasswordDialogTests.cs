using AssetHub.Ui.Tests.Helpers;
using Microsoft.AspNetCore.Components;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the SharePasswordDialog component.
/// Verifies password form rendering, validation, and save flow.
/// </summary>
public class SharePasswordDialogTests : BunitTestBase
{
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
        AccessCount = 0,
        HasPassword = hasPassword,
        Status = "Active"
    };

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
        AdminShareDto? share = null,
        string initialPassword = "")
    {
        share ??= CreateShare();

        var parameters = new DialogParameters<SharePasswordDialog>
        {
            { x => x.Share, share },
            { x => x.InitialPassword, initialPassword }
        };
        return await ShowDialogAsync<SharePasswordDialog>(parameters);
    }

    [Fact]
    public async Task Renders_Title_And_Password_Field()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("SharePassword", cut.Markup);
        Assert.True(cut.FindAll("input").Count >= 1);
    }

    [Fact]
    public async Task Shows_Warning_When_Password_Set_But_Not_Provided()
    {
        var share = CreateShare(hasPassword: true);
        var cut = await RenderDialogAsync(share, initialPassword: "");

        Assert.Contains("CurrentPasswordNotAvailable", cut.Markup);
    }

    [Fact]
    public async Task No_Warning_When_No_Existing_Password()
    {
        var share = CreateShare(hasPassword: false);
        var cut = await RenderDialogAsync(share, initialPassword: "");

        Assert.DoesNotContain("CurrentPasswordNotAvailable", cut.Markup);
    }

    [Fact]
    public async Task Has_Generate_And_Save_Buttons()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_GenerateNew", cut.Markup);
        Assert.Contains("UpdatePassword", cut.Markup);
    }

    [Fact]
    public async Task Has_Cancel_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Cancel", cut.Markup);
    }

    [Fact]
    public async Task Save_Button_Disabled_When_Password_Empty()
    {
        var cut = await RenderDialogAsync();

        var buttons = cut.FindAll("button");
        var saveButton = buttons.FirstOrDefault(b => b.TextContent.Contains("UpdatePassword"));
        Assert.NotNull(saveButton);
        Assert.True(saveButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Shows_Info_Alert_About_Password_Update_Consequences()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("PasswordUpdateWarning", cut.Markup);
    }

    [Fact]
    public async Task Pre_Fills_Password_When_InitialPassword_Provided()
    {
        var share = CreateShare(hasPassword: true);
        var cut = await RenderDialogAsync(share, initialPassword: "ExistingPass123");

        // The save button should be enabled when initial password meets min length
        var buttons = cut.FindAll("button");
        var saveButton = buttons.FirstOrDefault(b => b.TextContent.Contains("UpdatePassword"));
        Assert.NotNull(saveButton);
        Assert.False(saveButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Calls_UpdateSharePasswordAsync_On_Save()
    {
        var share = CreateShare();
        MockApi.Setup(a => a.UpdateSharePasswordAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cut = await RenderDialogAsync(share, initialPassword: "ValidPass123");

        var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("UpdatePassword"));
        await cut.InvokeAsync(() => saveButton.Click());

        MockApi.Verify(a => a.UpdateSharePasswordAsync(
            share.Id, "ValidPass123", It.IsAny<CancellationToken>()), Times.Once());
    }
}
