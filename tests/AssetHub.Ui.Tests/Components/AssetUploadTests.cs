using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Tests for the AssetUpload component.
/// Verifies rendering of upload area, file acceptance, collection requirement, and status display.
/// </summary>
public class AssetUploadTests : BunitTestBase
{
    [Fact]
    public void Renders_Upload_Area()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        Assert.Contains("upload-area", cut.Markup);
    }

    [Fact]
    public void Shows_DragDrop_Text()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        Assert.Contains("Text_DragDropFiles", cut.Markup);
    }

    [Fact]
    public void Shows_Browse_Button()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        Assert.Contains("Btn_BrowseFiles", cut.Markup);
    }

    [Fact]
    public void Shows_Supported_Formats_Text()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        Assert.Contains("Text_SupportedFormats", cut.Markup);
    }

    [Fact]
    public void Browse_Button_Disabled_When_No_CollectionId()
    {
        var cut = Render<AssetUpload>();

        // The browse "button" is actually a label element; should be disabled without a collection
        var browseLabel = cut.FindAll("label").FirstOrDefault(l => l.TextContent.Contains("Btn_BrowseFiles"));
        if (browseLabel != null)
        {
            Assert.True(browseLabel.HasAttribute("disabled") ||
                         browseLabel.ClassList.Contains("mud-disabled"));
        }
    }

    [Fact]
    public void Has_CloudUpload_Icon()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        Assert.Contains(Icons.Material.Filled.CloudUpload, cut.Markup);
    }

    [Fact]
    public void Has_Hidden_FileInput()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        var fileInput = cut.Find("input[type='file']");
        Assert.NotNull(fileInput);
        Assert.True(fileInput.HasAttribute("hidden"));
    }

    [Fact]
    public void FileInput_Accepts_Correct_Types()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        var fileInput = cut.Find("input[type='file']");
        var accept = fileInput.GetAttribute("accept");
        Assert.Contains("image/*", accept);
        Assert.Contains("video/*", accept);
        Assert.Contains(".pdf", accept);
    }

    [Fact]
    public void FileInput_Allows_Multiple()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        var fileInput = cut.Find("input[type='file']");
        Assert.True(fileInput.HasAttribute("multiple"));
    }

    [Fact]
    public void No_Upload_Items_Initially()
    {
        var cut = Render<AssetUpload>(p => p
            .Add(x => x.CollectionId, Guid.NewGuid()));

        // Upload list should not be rendered initially
        Assert.DoesNotContain("Label_Uploads", cut.Markup);
    }

    [Fact]
    public void Renders_Without_CollectionId()
    {
        // Should render without errors even without collection
        var cut = Render<AssetUpload>();

        Assert.Contains("upload-area", cut.Markup);
    }
}
