using AssetHub.Ui.Tests.Helpers;
using Microsoft.AspNetCore.Components;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the BulkCollectionActionsDialog component.
/// Verifies tab rendering, delete flow, and add access flow.
/// </summary>
public class BulkCollectionActionsDialogTests : BunitTestBase
{
    private void SetupUsers()
    {
        MockApi.Setup(a => a.GetKeycloakUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KeycloakUserDto>
            {
                new()
                {
                    Id = "user-1",
                    Username = "testuser",
                    Email = "test@example.com"
                }
            });
    }

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
        List<CollectionResponseDto>? collections = null)
    {
        SetupUsers();
        collections ??= TestData.CreateCollections(3, userRole: "admin");

        var parameters = new DialogParameters<BulkCollectionActionsDialog>
        {
            { x => x.SelectedCollections, collections }
        };
        return await ShowDialogAsync<BulkCollectionActionsDialog>(parameters);
    }

    [Fact]
    public async Task Renders_Title_And_Collection_Count()
    {
        var collections = TestData.CreateCollections(4);
        var cut = await RenderDialogAsync(collections);

        Assert.Contains("BulkActions", cut.Markup);
        Assert.Contains("BulkActionsDesc", cut.Markup);
    }

    [Fact]
    public async Task Renders_Delete_Tab()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("BulkDelete", cut.Markup);
        Assert.Contains("BulkDeleteAssetsSwitch", cut.Markup);
    }

    [Fact]
    public async Task Renders_Add_Access_Tab()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("BulkAddAccess", cut.Markup);
    }

    [Fact]
    public async Task Renders_Collection_List_In_Delete_Tab()
    {
        var collections = TestData.CreateCollections(2);
        var cut = await RenderDialogAsync(collections);

        Assert.Contains("CollectionName", cut.Markup);
        foreach (var c in collections)
        {
            Assert.Contains(c.Name, cut.Markup);
        }
    }

    [Fact]
    public async Task Shows_Delete_Assets_Switch()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("BulkDeleteAssetsSwitch", cut.Markup);
    }

    [Fact]
    public async Task Has_Close_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Close", cut.Markup);
    }

    [Fact]
    public async Task Loads_Users_On_Init()
    {
        await RenderDialogAsync();

        MockApi.Verify(a => a.GetKeycloakUsersAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handles_User_Load_Error()
    {
        MockApi.Setup(a => a.GetKeycloakUsersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API error"));

        var collections = TestData.CreateCollections(2);
        var parameters = new DialogParameters<BulkCollectionActionsDialog>
        {
            { x => x.SelectedCollections, collections }
        };
        await ShowDialogAsync<BulkCollectionActionsDialog>(parameters);

        VerifyHandleErrorCalled();
    }
}
