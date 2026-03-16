using AssetHub.Ui.Tests.Helpers;
using Microsoft.AspNetCore.Components;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the BulkAssetActionsDialog component.
/// Verifies asset list rendering, permanent delete toggle, and bulk delete flow.
/// </summary>
public class BulkAssetActionsDialogTests : BunitTestBase
{
    private readonly Guid _collectionId = Guid.NewGuid();

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
        List<AssetResponseDto>? assets = null,
        Guid? collectionId = null)
    {
        assets ??= TestData.CreateAssets(3);

        var parameters = new DialogParameters<BulkAssetActionsDialog>
        {
            { x => x.SelectedAssets, assets },
            { x => x.CollectionId, collectionId ?? _collectionId }
        };
        return await ShowDialogAsync<BulkAssetActionsDialog>(parameters);
    }

    [Fact]
    public async Task Renders_Title_And_Asset_Count()
    {
        var assets = TestData.CreateAssets(5);
        var cut = await RenderDialogAsync(assets);

        Assert.Contains("BulkAssetActions", cut.Markup);
        Assert.Contains("BulkAssetSelectedCount", cut.Markup);
    }

    [Fact]
    public async Task Renders_Asset_List_Table()
    {
        var assets = TestData.CreateAssets(3);
        var cut = await RenderDialogAsync(assets);

        // Table headers
        Assert.Contains("Column_Title", cut.Markup);
        Assert.Contains("Column_Size", cut.Markup);

        // Each asset title should appear
        foreach (var asset in assets)
        {
            Assert.Contains(asset.Title, cut.Markup);
        }
    }

    [Fact]
    public async Task Shows_Permanent_Delete_Switch_When_CollectionId_Present()
    {
        var cut = await RenderDialogAsync(collectionId: _collectionId);

        Assert.Contains("BulkAssetPermanentDeleteSwitch", cut.Markup);
    }

    [Fact]
    public async Task Hides_Permanent_Delete_Switch_When_No_CollectionId()
    {
        var assets = TestData.CreateAssets(2);
        var parameters = new DialogParameters<BulkAssetActionsDialog>
        {
            { x => x.SelectedAssets, assets },
            { x => x.CollectionId, null }
        };
        var cut = await ShowDialogAsync<BulkAssetActionsDialog>(parameters);

        Assert.DoesNotContain("BulkAssetPermanentDeleteSwitch", cut.Markup);
    }

    [Fact]
    public async Task Shows_Warning_Alert()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("BulkAssetDeleteWarning", cut.Markup);
    }

    [Fact]
    public async Task Has_Close_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("Btn_Close", cut.Markup);
    }

    [Fact]
    public async Task Has_Delete_Confirm_Button()
    {
        var cut = await RenderDialogAsync();

        Assert.Contains("BulkAssetDeleteConfirmBtn", cut.Markup);
    }
}
