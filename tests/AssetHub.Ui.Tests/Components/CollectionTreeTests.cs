using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the CollectionTree component.
/// Verifies rendering of collections, empty state, selection, deletion, and refresh.
/// </summary>
public class CollectionTreeTests : BunitTestBase
{
    [Fact]
    public void Shows_Loading_Indicator_Initially()
    {
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<List<CollectionResponseDto>>().Task);

        var cut = Render<CollectionTree>();

        Assert.NotNull(cut.Find(".mud-progress-linear"));
    }

    [Fact]
    public void Shows_EmptyState_When_No_Collections()
    {
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto>());

        var cut = Render<CollectionTree>();

        Assert.Contains("NoCollectionsYet", cut.Markup);
    }

    [Fact]
    public void Renders_Collection_Names()
    {
        var collections = TestData.CreateCollections(3);
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(collections);

        var cut = Render<CollectionTree>();

        foreach (var col in collections)
        {
            Assert.Contains(col.Name, cut.Markup);
        }
    }

    [Fact]
    public void Shows_Asset_Count_Badge()
    {
        var collection = TestData.CreateCollection(assetCount: 42);
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto> { collection });

        var cut = Render<CollectionTree>();

        Assert.Contains("42", cut.Markup);
    }

    [Fact]
    public void Hides_Asset_Count_When_Zero()
    {
        var collection = TestData.CreateCollection(assetCount: 0);
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto> { collection });

        var cut = Render<CollectionTree>();

        // Chip should not be rendered for 0 assets
        Assert.Empty(cut.FindAll(".mud-chip"));
    }

    [Fact]
    public void Clicking_Collection_Fires_OnCollectionSelected()
    {
        var collection = TestData.CreateCollection();
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto> { collection });

        Guid? selectedId = null;
        var cut = Render<CollectionTree>(p => p
            .Add(x => x.OnCollectionSelected, (Guid? id) => selectedId = id));

        // Click the nav link
        var navLink = cut.Find(".mud-nav-link");
        navLink.Click();

        Assert.Equal(collection.Id, selectedId);
    }

    [Fact]
    public void Highlights_Selected_Collection()
    {
        var collections = TestData.CreateCollections(3);
        var selectedId = collections[1].Id;
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(collections);

        var cut = Render<CollectionTree>(p => p
            .Add(x => x.SelectedCollectionId, selectedId));

        // The selected collection should have an active class
        Assert.Contains("mud-nav-link-active", cut.Markup);
    }

    [Fact]
    public void Renders_Collection_Without_Context_Menu()
    {
        var collection = TestData.CreateCollection();
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto> { collection });

        var cut = Render<CollectionTree>();

        // Context menu was removed — actions are on the collection header instead
        Assert.DoesNotContain(Icons.Material.Filled.MoreVert, cut.Markup);
    }

    [Fact]
    public void Handles_Api_Error_Gracefully()
    {
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        Render<CollectionTree>();

        VerifyHandleErrorCalled();
    }

    [Fact]
    public void Renders_Folder_Icons()
    {
        var collection = TestData.CreateCollection();
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CollectionResponseDto> { collection });

        var cut = Render<CollectionTree>();

        Assert.Contains(Icons.Material.Filled.Folder, cut.Markup);
    }

    [Fact]
    public void Multiple_Collections_All_Rendered()
    {
        var collections = TestData.CreateCollections(10);
        MockApi.Setup(a => a.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(collections);

        var cut = Render<CollectionTree>();

        var navLinks = cut.FindAll(".mud-nav-link");
        Assert.Equal(10, navLinks.Count);
    }
}
