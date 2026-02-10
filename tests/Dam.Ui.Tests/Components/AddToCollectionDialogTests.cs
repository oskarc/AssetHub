using Dam.Ui.Tests.Helpers;

namespace Dam.Ui.Tests.Components;

/// <summary>
/// Tests for the AddToCollectionDialog component.
/// Verifies loading collections, filtering existing, selection, and submission.
/// </summary>
public class AddToCollectionDialogTests : BunitTestBase
{
    private readonly Guid _assetId = Guid.NewGuid();

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
        List<Guid>? existingCollectionIds = null)
    {
        var parameters = new DialogParameters<AddToCollectionDialog>
        {
            { x => x.AssetId, _assetId },
            { x => x.ExistingCollectionIds, existingCollectionIds ?? new List<Guid>() }
        };
        return await ShowDialogAsync<AddToCollectionDialog>(parameters);
    }

    [Fact]
    public async Task Shows_Loading_While_Fetching_Collections()
    {
        MockApi.Setup(a => a.GetCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<List<CollectionResponseDto>>().Task);

        var cut = await RenderDialogAsync();

        Assert.NotNull(cut.Find(".mud-progress-circular"));
    }

    [Fact]
    public async Task Shows_Info_Alert_When_No_Additional_Collections()
    {
        var existingId = Guid.NewGuid();
        var collections = new List<CollectionResponseDto>
        {
            TestData.CreateCollection(id: existingId)
        };
        MockApi.Setup(a => a.GetCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(collections);

        var cut = await RenderDialogAsync(existingCollectionIds: [existingId]);

        Assert.Contains("NoAdditionalCollections", cut.Markup);
    }

    [Fact]
    public async Task Filters_Out_Existing_Collections()
    {
        var existingId = Guid.NewGuid();
        var availableId = Guid.NewGuid();
        var collections = new List<CollectionResponseDto>
        {
            TestData.CreateCollection(id: existingId, name: "Already In"),
            TestData.CreateCollection(id: availableId, name: "Available")
        };
        MockApi.Setup(a => a.GetCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(collections);

        var cut = await RenderDialogAsync(existingCollectionIds: [existingId]);

        // "Available" should be visible but "Already In" should not
        Assert.DoesNotContain("Already In", cut.Markup);
    }

    [Fact]
    public async Task Has_Cancel_And_Add_Buttons()
    {
        MockApi.Setup(a => a.GetCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto> { TestData.CreateCollection() });

        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Cancel", cut.Markup);
        Assert.Contains("Btn_Add", cut.Markup);
    }

    [Fact]
    public async Task Add_Button_Disabled_When_No_Selection()
    {
        MockApi.Setup(a => a.GetCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto> { TestData.CreateCollection() });

        var cut = await RenderDialogAsync();

        // The add button should be disabled until a collection is selected
        var addButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Btn_Add"));
        Assert.NotNull(addButton);
        Assert.True(addButton.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Shows_Select_Collection_Prompt()
    {
        MockApi.Setup(a => a.GetCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto> { TestData.CreateCollection() });

        var cut = await RenderDialogAsync();

        Assert.Contains("SelectCollectionToAdd", cut.Markup);
    }

    [Fact]
    public async Task Handles_Api_Error_Gracefully()
    {
        MockApi.Setup(a => a.GetCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        var cut = await RenderDialogAsync();

        VerifyHandleErrorCalled();
    }

    [Fact]
    public async Task Multiple_Available_Collections_All_Rendered()
    {
        var collections = TestData.CreateCollections(5);
        MockApi.Setup(a => a.GetCollectionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(collections);

        var cut = await RenderDialogAsync();

        // Open the MudSelect dropdown (MudBlazor 8 uses mousedown)
        cut.Find("div.mud-select div.mud-input-control").MouseDown();

        // MudSelectItem contents now render inside MudPopoverProvider
        foreach (var col in collections)
        {
            Assert.Contains(col.Name, PopoverProvider!.Markup);
        }
    }
}
