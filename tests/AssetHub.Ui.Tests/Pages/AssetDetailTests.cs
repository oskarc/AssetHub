using AssetHub.Ui.Pages;
using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Pages;

/// <summary>
/// Tests for the AssetDetail page's action panel.
/// </summary>
public class AssetDetailTests : BunitTestBase
{
    [Fact]
    public void AssetDetail_ForManager_ShowsExactlyTheSupportedActions()
    {
        var id = Guid.NewGuid();
        MockApi.Setup(a => a.GetAssetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAsset(id: id, userRole: "manager"));
        MockApi.Setup(a => a.GetAssetCollectionsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssetCollectionDto>());

        var cut = Render<AssetDetail>(p => p.Add(x => x.AssetId, id));

        // The action column, in order. The stub localizer echoes keys, so labels
        // are the resource keys. A manager can download, share, edit and delete.
        var actions = cut.WaitForElement("a[href$='/download']").ParentElement!;
        var labels = actions.QuerySelectorAll(".mud-button-label").Select(e => e.TextContent.Trim()).ToArray();

        Assert.Equal(new[] { "Btn_DownloadOriginal", "Btn_Share", "EditDetails", "Btn_Delete" }, labels);
    }
}
