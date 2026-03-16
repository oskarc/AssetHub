using AssetHub.Ui.Tests.Helpers;
using Microsoft.AspNetCore.Components;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the DeleteAssetDialog component.
/// Verifies context-aware UI (single vs multi-collection) and action results.
/// </summary>
public class DeleteAssetDialogTests : BunitTestBase
{
    private readonly Guid _assetId = Guid.NewGuid();
    private readonly Guid _collectionId = Guid.NewGuid();

    private async Task<IRenderedComponent<MudDialogProvider>> RenderDialogAsync(
        AssetDeletionContextDto context)
    {
        MockApi.Setup(a => a.GetAssetDeletionContextAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        var parameters = new DialogParameters<DeleteAssetDialog>
        {
            { x => x.AssetId, _assetId },
            { x => x.AssetTitle, "My Asset" },
            { x => x.FromCollectionId, _collectionId }
        };
        return await ShowDialogAsync<DeleteAssetDialog>(parameters);
    }

    [Fact]
    public async Task Single_Collection_Shows_Simple_Confirm()
    {
        var cut = await RenderDialogAsync(new AssetDeletionContextDto
        {
            CollectionCount = 1,
            CanDeletePermanently = true
        });

        Assert.Contains("ConfirmDeleteAsset", cut.Markup);
        Assert.Contains("Btn_Cancel", cut.Markup);
        Assert.Contains("Btn_Delete", cut.Markup);
    }

    [Fact]
    public async Task Multi_Collection_Shows_Remove_And_Delete_Options()
    {
        var cut = await RenderDialogAsync(new AssetDeletionContextDto
        {
            CollectionCount = 3,
            CanDeletePermanently = true
        });

        Assert.Contains("DeleteOrRemove", cut.Markup);
        Assert.Contains("Btn_RemoveFromCollection", cut.Markup);
        Assert.Contains("Btn_DeletePermanently", cut.Markup);
        Assert.Contains("Btn_Remove", cut.Markup);
        Assert.Contains("Btn_Delete", cut.Markup);
    }

    [Fact]
    public async Task Multi_Collection_Shows_Partial_Delete_Hint_When_Cannot_Delete_Permanently()
    {
        var cut = await RenderDialogAsync(new AssetDeletionContextDto
        {
            CollectionCount = 2,
            CanDeletePermanently = false
        });

        Assert.Contains("Text_DeletePartialHint", cut.Markup);
    }

    [Fact]
    public async Task Falls_Back_To_Simple_Delete_On_Api_Error()
    {
        MockApi.Setup(a => a.GetAssetDeletionContextAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API error"));

        var parameters = new DialogParameters<DeleteAssetDialog>
        {
            { x => x.AssetId, _assetId },
            { x => x.AssetTitle, "My Asset" },
            { x => x.FromCollectionId, _collectionId }
        };
        var cut = await ShowDialogAsync<DeleteAssetDialog>(parameters);

        // Falls back to simple delete (CollectionCount = 1)
        Assert.Contains("ConfirmDeleteAsset", cut.Markup);
        VerifyHandleErrorCalled();
    }

    [Fact]
    public async Task Calls_GetAssetDeletionContextAsync_On_Init()
    {
        await RenderDialogAsync(new AssetDeletionContextDto
        {
            CollectionCount = 1,
            CanDeletePermanently = true
        });

        MockApi.Verify(a => a.GetAssetDeletionContextAsync(
            _assetId, It.IsAny<CancellationToken>()), Times.Once());
    }
}
