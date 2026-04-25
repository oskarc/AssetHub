using AssetHub.Ui.Tests.Helpers;
using Microsoft.AspNetCore.Components;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the CreateUserDialog component.
/// Verifies form rendering, validation, system admin toggle, and user creation flow.
/// </summary>
public class CreateUserDialogTests : BunitTestBase
{
    private void SetupCollections()
    {
        MockApi.Setup(a => a.GetCollectionAccessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionAccessDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Collection",
                    Acls = []
                }
            });
    }

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync()
    {
        SetupCollections();
        var parameters = new DialogParameters<CreateUserDialog>();
        return await ShowDialogAsync<CreateUserDialog>(parameters);
    }

    [Fact]
    public async Task Renders_Title_And_Form_Fields()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("CreateNewUser", cut.Markup);
        // Should have Username, Email, FirstName, LastName inputs
        Assert.True(cut.FindAll("input").Count >= 4);
    }

    [Fact]
    public async Task Has_Cancel_And_Create_Buttons()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Cancel", cut.Markup);
        Assert.Contains("Btn_CreateUser", cut.Markup);
    }

    [Fact]
    public async Task Create_Button_Initially_Disabled()
    {
        var cut = await RenderDialogAsync();

        var buttons = cut.FindAll("button");
        var createButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Btn_CreateUser"));
        Assert.NotNull(createButton);
        Assert.True(createButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Shows_System_Admin_Checkbox()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("MakeAdministrator", cut.Markup);
    }

    [Fact]
    public async Task Loads_Collections_On_Init()
    {
        await RenderDialogAsync();

        MockApi.Verify(a => a.GetCollectionAccessAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Shows_PersonAdd_Icon()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains(Icons.Material.Filled.PersonAdd, cut.Markup);
    }

    [Fact]
    public async Task Handles_Collection_Load_Error()
    {
        MockApi.Setup(a => a.GetCollectionAccessAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API error"));

        var parameters = new DialogParameters<CreateUserDialog>();
        await ShowDialogAsync<CreateUserDialog>(parameters);

        VerifyHandleErrorCalled();
    }
}
