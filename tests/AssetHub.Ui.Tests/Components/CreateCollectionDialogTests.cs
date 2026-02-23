using AssetHub.Ui.Tests.Helpers;
using Microsoft.AspNetCore.Components;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the CreateCollectionDialog component.
/// Verifies form rendering, validation, and creation flow.
/// </summary>
public class CreateCollectionDialogTests : BunitTestBase
{
    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync()
    {
        var parameters = new DialogParameters<CreateCollectionDialog>();
        return await ShowDialogAsync<CreateCollectionDialog>(parameters);
    }

    [Fact]
    public async Task Renders_Dialog_With_Title_And_Form()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("CreateCollection", cut.Markup);
        // Should have name and description fields
        Assert.True(cut.FindAll("input, textarea").Count >= 1);
    }

    [Fact]
    public async Task Has_Cancel_And_Create_Buttons()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Cancel", cut.Markup);
        Assert.Contains("Btn_Create", cut.Markup);
    }

    [Fact]
    public async Task Create_Button_Initially_Disabled_Until_Form_Valid()
    {
        var cut = await RenderDialogAsync();

        // The create button requires form validation (_isValid)
        // Name is required so initially the button should be disabled
        var buttons = cut.FindAll("button");
        var createButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Btn_Create"));
        Assert.NotNull(createButton);
        // Button has Disabled attribute when form is not valid
        Assert.True(createButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Calls_CreateCollectionAsync_On_Submit()
    {
        var result = TestData.CreateCollection(name: "New Collection");
        MockApi.Setup(a => a.CreateCollectionAsync(It.IsAny<CreateCollectionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var cut = await RenderDialogAsync();

        // Fill in the name field
        var nameInput = cut.Find("input");
        nameInput.Change("New Collection");
        nameInput.Blur();

        // Wait for form to become valid, then click the Create button
        cut.WaitForState(() => cut.FindAll("button").Any(b =>
            b.TextContent.Contains("Btn_Create") && !b.HasAttribute("disabled")),
            TimeSpan.FromSeconds(2));

        var createButton = cut.FindAll("button").First(b => b.TextContent.Contains("Btn_Create"));
        await cut.InvokeAsync(() => createButton.Click());

        MockApi.Verify(a => a.CreateCollectionAsync(
            It.Is<CreateCollectionDto>(dto => dto.Name == "New Collection"),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Renders_CreateNewFolder_Icon()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains(Icons.Material.Filled.CreateNewFolder, cut.Markup);
    }
}
