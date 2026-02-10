using Dam.Ui.Tests.Helpers;

namespace Dam.Ui.Tests.Components;

/// <summary>
/// Tests for the AssetGrid component.
/// Verifies rendering of asset cards, loading state, empty state, pagination, 
/// role-based action visibility, and navigation.
/// </summary>
public class AssetGridTests : BunitTestBase
{
    private readonly Guid _collectionId = Guid.NewGuid();

    [Fact]
    public void Shows_Loading_Spinner_While_Fetching()
    {
        // Never complete the API call
        MockApi.Setup(a => a.GetAssetsAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<AssetListResponse>().Task);

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        Assert.NotNull(cut.Find(".mud-progress-circular"));
    }

    [Fact]
    public void Shows_EmptyState_When_No_Assets()
    {
        MockApi.Setup(a => a.GetAssetsAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 0, Items = [] });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        // Should show EmptyState with localized key
        Assert.Contains("NoAssetsYet", cut.Markup);
    }

    [Fact]
    public void Renders_Asset_Cards_With_Titles()
    {
        var assets = TestData.CreateAssets(3, userRole: "viewer");
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), 0, 24, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 3, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        var cards = cut.FindAll(".asset-card");
        Assert.Equal(3, cards.Count);

        foreach (var asset in assets)
        {
            Assert.Contains(asset.Title, cut.Markup);
        }
    }

    [Fact]
    public void Renders_AssetType_Chip()
    {
        var assets = new List<AssetResponseDto> { TestData.CreateAsset(assetType: "image") };
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 1, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        Assert.Contains("image", cut.Markup);
    }

    [Fact]
    public void Shows_LoadMore_Button_When_More_Assets_Available()
    {
        var assets = TestData.CreateAssets(5);
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 30, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        // Should show load more with remaining count
        Assert.Contains("Btn_LoadMore", cut.Markup);
        Assert.Contains("25 remaining", cut.Markup);
    }

    [Fact]
    public void Hides_LoadMore_When_All_Assets_Loaded()
    {
        var assets = TestData.CreateAssets(3);
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 3, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        Assert.DoesNotContain("Btn_LoadMore", cut.Markup);
    }

    [Fact]
    public void Viewer_Cannot_See_Share_Button()
    {
        var assets = new List<AssetResponseDto> { TestData.CreateAsset() };
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 1, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        // Share icon should not be present for viewer
        var shareButtons = cut.FindAll("button").Where(b => b.OuterHtml.Contains("Share")).ToList();
        // Viewer = no share, no delete
        var deleteButtons = cut.FindAll("button").Where(b =>
            b.ClassList.Contains("mud-icon-button") &&
            b.OuterHtml.Contains("color-error")).ToList();
        Assert.Empty(deleteButtons);
    }

    [Fact]
    public void Contributor_Can_See_Share_Button()
    {
        var assets = new List<AssetResponseDto> { TestData.CreateAsset() };
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 1, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "contributor"));

        // Contributor can share but cannot delete
        Assert.Contains(Icons.Material.Filled.Share, cut.Markup);
    }

    [Fact]
    public void Manager_Can_See_Delete_Button()
    {
        var assets = new List<AssetResponseDto> { TestData.CreateAsset() };
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 1, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "manager"));

        // Manager can see both share and delete
        Assert.Contains(Icons.Material.Filled.Share, cut.Markup);
        Assert.Contains(Icons.Material.Filled.Delete, cut.Markup);
    }

    [Fact]
    public void Clicking_Asset_Card_Navigates_To_Detail()
    {
        var assetId = Guid.NewGuid();
        var assets = new List<AssetResponseDto> { TestData.CreateAsset(id: assetId) };
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 1, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        // Click the card container (not a button)
        var cardDiv = cut.Find(".asset-card div[style*='cursor: pointer']");
        cardDiv.Click();

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.Contains($"/assets/{assetId}", nav.Uri);
    }

    [Fact]
    public void Shows_File_Size_For_Each_Asset()
    {
        var asset = TestData.CreateAsset(sizeBytes: 1048576); // 1 MB
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 1, Items = [asset] });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        // Should display formatted size
        Assert.Contains("MB", cut.Markup);
    }

    [Fact]
    public void Handles_Api_Error_Gracefully()
    {
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Server error"));

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "viewer"));

        VerifyHandleErrorCalled();
    }

    [Fact]
    public void No_CollectionId_Shows_No_Content()
    {
        var cut = Render<AssetGrid>(p => p
            .Add(x => x.UserRole, "viewer"));

        // Without a CollectionId, no loading and no assets
        Assert.Empty(cut.FindAll(".asset-card"));
        Assert.Empty(cut.FindAll(".mud-progress-circular"));
    }

    [Fact]
    public void Admin_Can_See_All_Actions()
    {
        var assets = new List<AssetResponseDto> { TestData.CreateAsset() };
        MockApi.Setup(a => a.GetAssetsAsync(
                _collectionId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetListResponse { CollectionId = _collectionId, Total = 1, Items = assets });

        var cut = Render<AssetGrid>(p => p
            .Add(x => x.CollectionId, _collectionId)
            .Add(x => x.UserRole, "admin"));

        // Admin can share and delete
        Assert.Contains(Icons.Material.Filled.Share, cut.Markup);
        Assert.Contains(Icons.Material.Filled.Delete, cut.Markup);
    }
}
