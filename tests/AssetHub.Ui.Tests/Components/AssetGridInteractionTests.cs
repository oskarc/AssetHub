using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for AssetGrid interactive workflows: share and delete button chains.
/// Uses a mock IDialogService (overriding MudBlazor's) to verify dialog invocations.
/// </summary>
public class AssetGridInteractionTests : BunitTestBase
{
    private readonly Guid _collectionId = Guid.NewGuid();

    public AssetGridInteractionTests()
    {
        // Override MudBlazor's dialog service with our mock for interaction testing.
        // This registration happens after AddMudServices() in the base constructor,
        // so the mock takes precedence in DI resolution.
        Services.AddSingleton<IDialogService>(MockDialogService.Object);
    }

    private void SetupAssets(List<AssetResponseDto>? assets = null)
    {
        assets ??= new List<AssetResponseDto> { TestData.CreateAsset() };
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = assets.Count, Items = assets });
    }

    /// <summary>
    /// Sets up the mock dialog service to return a canceled dialog result for the given dialog type.
    /// MudBlazor 8's IDialogService.ShowAsync&lt;T&gt; uses non-generic DialogParameters in the
    /// 2-arg overload (string?, DialogParameters), which is what AssetGrid calls.
    /// </summary>
    private void SetupCanceledDialog<TDialog>() where TDialog : Microsoft.AspNetCore.Components.ComponentBase
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.Setup(d => d.Result).Returns(Task.FromResult<DialogResult?>(DialogResult.Cancel()));
        // The component calls the 2-arg overload: ShowAsync<T>(title, parameters)
        MockDialogService
            .Setup(d => d.ShowAsync<TDialog>(
                It.IsAny<string?>(),
                It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
    }

    [Fact]
    public void Share_Button_Click_Opens_CreateShareDialog()
    {
        SetupAssets();
        SetupCanceledDialog<CreateShareDialog>();

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "contributor"));

        var cardActions = cut.Find(".mud-card-actions");
        var iconButtons = cardActions.QuerySelectorAll("button.mud-icon-button");
        Assert.True(iconButtons.Length >= 1, $"Expected at least 1 icon button, got {iconButtons.Length}");
        var shareButton = iconButtons[0]; // Share is the first icon button for contributor
        shareButton.Click();

        MockDialogService.Verify(d => d.ShowAsync<CreateShareDialog>(
            It.IsAny<string?>(),
            It.IsAny<DialogParameters>()), Times.Once);
    }

    [Fact]
    public void Delete_Button_Click_Opens_DeleteAssetDialog()
    {
        SetupAssets();
        SetupCanceledDialog<DeleteAssetDialog>();

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "manager"));

        var cardActions = cut.Find(".mud-card-actions");
        var iconButtons = cardActions.QuerySelectorAll("button.mud-icon-button");
        var deleteButton = iconButtons[iconButtons.Length - 1]; // Last icon button = delete
        deleteButton.Click();

        MockDialogService.Verify(d => d.ShowAsync<DeleteAssetDialog>(
            It.IsAny<string?>(),
            It.IsAny<DialogParameters>()), Times.Once);
    }
}
