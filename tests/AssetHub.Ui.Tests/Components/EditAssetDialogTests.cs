using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the EditAssetDialog component.
/// Verifies pre-populated fields, tag management, save flow, and validation.
/// </summary>
public class EditAssetDialogTests : BunitTestBase
{
    private readonly AssetResponseDto _testAsset;

    public EditAssetDialogTests()
    {
        _testAsset = new AssetResponseDto
        {
            Id = Guid.NewGuid(),
            Title = "Original Title",
            Description = "Original Description",
            AssetType = "image",
            Status = "ready",
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            Tags = new List<string> { "tag1", "tag2", "tag3" },
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1",
            UpdatedAt = DateTime.UtcNow,
            MetadataJson = new Dictionary<string, object>()
        };
    }

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(AssetResponseDto? asset = null)
    {
        var parameters = new DialogParameters<EditAssetDialog>
        {
            { x => x.Asset, asset ?? _testAsset }
        };
        return await ShowDialogAsync<EditAssetDialog>(parameters);
    }

    [Fact]
    public async Task Pre_Populates_Title_From_Asset()
    {
        var cut = await RenderDialogAsync();

        var inputs = cut.FindAll("input");
        var titleInput = inputs.FirstOrDefault(i => i.GetAttribute("value") == "Original Title");
        Assert.NotNull(titleInput);
    }

    [Fact]
    public async Task Pre_Populates_Description_From_Asset()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Original Description", cut.Markup);
    }

    [Fact]
    public async Task Renders_Existing_Tags_As_Chips()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("tag1", cut.Markup);
        Assert.Contains("tag2", cut.Markup);
        Assert.Contains("tag3", cut.Markup);
    }

    [Fact]
    public async Task Shows_NoTags_Message_When_Empty()
    {
        var assetNoTags = TestData.CreateAsset();
        assetNoTags.Tags = [];

        var cut = await RenderDialogAsync(assetNoTags);

        Assert.Contains("NoTags", cut.Markup);
    }

    [Fact]
    public async Task Has_Cancel_And_Save_Buttons()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Cancel", cut.Markup);
        Assert.Contains("Btn_SaveChanges", cut.Markup);
    }

    [Fact]
    public async Task Has_AddTag_Input_And_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Label_AddTag", cut.Markup);
        Assert.Contains("Btn_Add", cut.Markup);
    }

    [Fact]
    public async Task Renders_Edit_Icon_In_Title()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains(Icons.Material.Filled.Edit, cut.Markup);
        Assert.Contains("EditAssetDetails", cut.Markup);
    }

    [Fact]
    public async Task Shows_Title_Required_Label()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Label_Title", cut.Markup);
    }

    [Fact]
    public async Task Shows_Tags_Label()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Label_Tags", cut.Markup);
    }

    [Fact]
    public async Task Asset_With_Many_Tags_Renders_All()
    {
        var asset = TestData.CreateAsset();
        asset.Tags = Enumerable.Range(1, 10).Select(i => $"tag-{i}").ToList();

        var cut = await RenderDialogAsync(asset);

        for (int i = 1; i <= 10; i++)
        {
            Assert.Contains($"tag-{i}", cut.Markup);
        }
    }

    [Fact]
    public async Task Handles_Null_Description_In_Asset()
    {
        var asset = TestData.CreateAsset(description: null);

        var cut = await RenderDialogAsync(asset);

        // Should render without error
        Assert.Contains("EditAssetDetails", cut.Markup);
    }

    [Fact]
    public async Task Handles_Null_Tags_In_Asset()
    {
        var asset = TestData.CreateAsset();
        asset.Tags = null!;

        var cut = await RenderDialogAsync(asset);

        Assert.Contains("NoTags", cut.Markup);
    }

    // ── Save submission flow ────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_Calls_UpdateAssetAsync_With_CurrentValues()
    {
        var updatedAsset = new AssetResponseDto
        {
            Id = _testAsset.Id,
            Title = "Original Title",
            Description = "Original Description",
            AssetType = "image",
            Status = "ready",
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            Tags = new List<string> { "tag1", "tag2", "tag3" },
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1",
            UpdatedAt = DateTime.UtcNow,
            MetadataJson = new Dictionary<string, object>()
        };

        MockApi.Setup(a => a.UpdateAssetAsync(
                _testAsset.Id,
                It.IsAny<UpdateAssetDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedAsset);

        var cut = await RenderDialogAsync();

        // MudForm starts with _isValid=false. Trigger validation by interacting with the required title field.
        var titleInput = cut.FindAll("input")
            .First(i => i.GetAttribute("value") == "Original Title");
        // Re-enter the value to trigger validation
        await cut.InvokeAsync(() => titleInput.Change("Original Title"));
        await cut.InvokeAsync(() => titleInput.Blur());

        // Wait for MudForm to validate
        cut.WaitForState(() =>
        {
            var saveBtn = cut.FindAll("button")
                .FirstOrDefault(b => b.TextContent.Contains("Btn_SaveChanges"));
            return saveBtn != null && !saveBtn.HasAttribute("disabled");
        }, TimeSpan.FromSeconds(2));

        // Click Save button (now enabled)
        var saveButton = cut.FindAll("button")
            .First(b => b.TextContent.Contains("Btn_SaveChanges"));
        await cut.InvokeAsync(() => saveButton.Click());

        MockApi.Verify(a => a.UpdateAssetAsync(
            _testAsset.Id,
            It.Is<UpdateAssetDto>(dto =>
                dto.Title == "Original Title" &&
                dto.Description == "Original Description"),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task SaveAsync_Error_Calls_HandleError()
    {
        MockApi.Setup(a => a.UpdateAssetAsync(
                _testAsset.Id,
                It.IsAny<UpdateAssetDto>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        var cut = await RenderDialogAsync();

        // Trigger validation
        var titleInput = cut.FindAll("input")
            .First(i => i.GetAttribute("value") == "Original Title");
        await cut.InvokeAsync(() => titleInput.Change("Original Title"));
        await cut.InvokeAsync(() => titleInput.Blur());

        cut.WaitForState(() =>
        {
            var btn = cut.FindAll("button")
                .FirstOrDefault(b => b.TextContent.Contains("Btn_SaveChanges"));
            return btn != null && !btn.HasAttribute("disabled");
        }, TimeSpan.FromSeconds(2));

        var saveBtn = cut.FindAll("button")
            .First(b => b.TextContent.Contains("Btn_SaveChanges"));
        await cut.InvokeAsync(() => saveBtn.Click());

        VerifyHandleErrorCalled();
    }
}
