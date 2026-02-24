namespace AssetHub.Ui.Tests.Services;

/// <summary>
/// Tests for the AssetDisplayHelpers static utility class.
/// Verifies thumbnail URLs, type colors/icons, status colors, formatting.
/// </summary>
public class AssetDisplayHelpersTests
{
    // ===== GetThumbnailUrl =====

    [Fact]
    public void GetThumbnailUrl_Returns_ApiUrl_When_ThumbKey_Exists()
    {
        var id = Guid.NewGuid();
        var url = AssetDisplayHelpers.GetThumbnailUrl(id, "thumbs/abc.jpg", "image");

        Assert.Equal($"/api/assets/{id}/thumb", url);
    }

    [Fact]
    public void GetThumbnailUrl_Returns_Placeholder_When_No_ThumbKey()
    {
        var url = AssetDisplayHelpers.GetThumbnailUrl(Guid.NewGuid(), null, "image");

        Assert.StartsWith("data:image/svg+xml,", url);
        Assert.Contains("Image", url);
    }

    [Fact]
    public void GetThumbnailUrl_Returns_Placeholder_When_Empty_ThumbKey()
    {
        var url = AssetDisplayHelpers.GetThumbnailUrl(Guid.NewGuid(), "", "video");

        Assert.StartsWith("data:image/svg+xml,", url);
        Assert.Contains("Video", url);
    }

    // ===== GetPlaceholderForType =====

    [Theory]
    [InlineData("image", "Image")]
    [InlineData("video", "Video")]
    [InlineData("document", "Document")]
    [InlineData("unknown", "Asset")]
    [InlineData(null, "Asset")]
    public void GetPlaceholderForType_Returns_Correct_SVG(string? assetType, string expectedLabel)
    {
        var svg = AssetDisplayHelpers.GetPlaceholderForType(assetType);

        Assert.StartsWith("data:image/svg+xml,", svg);
        Assert.Contains(expectedLabel, Uri.UnescapeDataString(svg));
    }

    // ===== GetAssetTypeColor =====

    [Theory]
    [InlineData("image", Color.Success)]
    [InlineData("video", Color.Info)]
    [InlineData("document", Color.Warning)]
    [InlineData("unknown", Color.Default)]
    [InlineData(null, Color.Default)]
    public void GetAssetTypeColor_Returns_Correct_Color(string? assetType, Color expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.GetAssetTypeColor(assetType));
    }

    // ===== GetAssetIcon =====

    [Fact]
    public void GetAssetIcon_Image_Returns_ImageIcon()
    {
        var icon = AssetDisplayHelpers.GetAssetIcon("image");
        Assert.Equal(Icons.Material.Filled.Image, icon);
    }

    [Fact]
    public void GetAssetIcon_Video_Returns_VideoIcon()
    {
        var icon = AssetDisplayHelpers.GetAssetIcon("video");
        Assert.Equal(Icons.Material.Filled.VideoFile, icon);
    }

    [Fact]
    public void GetAssetIcon_Document_Returns_DescriptionIcon()
    {
        var icon = AssetDisplayHelpers.GetAssetIcon("document");
        Assert.Equal(Icons.Material.Filled.Description, icon);
    }

    [Fact]
    public void GetAssetIcon_Unknown_Returns_GenericIcon()
    {
        var icon = AssetDisplayHelpers.GetAssetIcon("other");
        Assert.Equal(Icons.Material.Filled.InsertDriveFile, icon);
    }

    // ===== GetContentTypeIcon =====

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void GetContentTypeIcon_ImageTypes_Return_ImageIcon(string contentType)
    {
        Assert.Equal(Icons.Material.Filled.Image, AssetDisplayHelpers.GetContentTypeIcon(contentType));
    }

    [Theory]
    [InlineData("video/mp4")]
    [InlineData("video/webm")]
    public void GetContentTypeIcon_VideoTypes_Return_VideoIcon(string contentType)
    {
        Assert.Equal(Icons.Material.Filled.VideoFile, AssetDisplayHelpers.GetContentTypeIcon(contentType));
    }

    [Fact]
    public void GetContentTypeIcon_Pdf_Returns_PdfIcon()
    {
        Assert.Equal(Icons.Material.Filled.PictureAsPdf, AssetDisplayHelpers.GetContentTypeIcon("application/pdf"));
    }

    [Fact]
    public void GetContentTypeIcon_Null_Returns_GenericIcon()
    {
        Assert.Equal(Icons.Material.Filled.InsertDriveFile, AssetDisplayHelpers.GetContentTypeIcon(null));
    }

    // ===== GetAssetStatusColor =====

    [Theory]
    [InlineData("ready", Color.Success)]
    [InlineData("processing", Color.Info)]
    [InlineData("failed", Color.Error)]
    [InlineData("unknown", Color.Default)]
    [InlineData(null, Color.Default)]
    public void GetAssetStatusColor_Returns_Correct_Color(string? status, Color expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.GetAssetStatusColor(status));
    }

    // ===== GetShareStatusColor =====

    [Theory]
    [InlineData("Active", Color.Success)]
    [InlineData("Expired", Color.Warning)]
    [InlineData("Revoked", Color.Error)]
    [InlineData("Unknown", Color.Default)]
    [InlineData(null, Color.Default)]
    public void GetShareStatusColor_Returns_Correct_Color(string? status, Color expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.GetShareStatusColor(status));
    }

    // ===== GetRoleColor =====

    [Theory]
    [InlineData("admin", Color.Error)]
    [InlineData("manager", Color.Warning)]
    [InlineData("contributor", Color.Info)]
    [InlineData("viewer", Color.Default)]
    [InlineData("unknown", Color.Default)]
    [InlineData(null, Color.Default)]
    public void GetRoleColor_Returns_Correct_Color(string? role, Color expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.GetRoleColor(role));
    }

    // ===== GetRoleKey =====

    [Theory]
    [InlineData("viewer", "Role_Viewer")]
    [InlineData("contributor", "Role_Contributor")]
    [InlineData("manager", "Role_Manager")]
    [InlineData("admin", "Role_Admin")]
    [InlineData("unknown", "unknown")]
    [InlineData(null, "")]
    public void GetRoleKey_Returns_Correct_Resource_Key(string? role, string expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.GetRoleKey(role));
    }

    // ===== GetShareStatusKey =====

    [Theory]
    [InlineData("Active", "Active")]
    [InlineData("Expired", "Expired")]
    [InlineData("Revoked", "Revoked")]
    [InlineData("Unknown", "Unknown")]
    [InlineData(null, "")]
    public void GetShareStatusKey_Returns_Correct_Resource_Key(string? status, string expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.GetShareStatusKey(status));
    }

    // ===== GetAssetTypeKey =====

    [Theory]
    [InlineData("image", "AssetType_Image")]
    [InlineData("video", "AssetType_Video")]
    [InlineData("document", "AssetType_Document")]
    [InlineData("audio", "AssetType_Audio")]
    [InlineData("unknown", "unknown")]
    [InlineData(null, "")]
    public void GetAssetTypeKey_Returns_Correct_Resource_Key(string? assetType, string expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.GetAssetTypeKey(assetType));
    }

    // ===== GetAssetStatusKey =====

    [Theory]
    [InlineData("ready", "AssetStatus_Ready")]
    [InlineData("processing", "AssetStatus_Processing")]
    [InlineData("failed", "AssetStatus_Failed")]
    [InlineData("unknown", "unknown")]
    [InlineData(null, "")]
    public void GetAssetStatusKey_Returns_Correct_Resource_Key(string? status, string expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.GetAssetStatusKey(status));
    }

    // ===== FormatFileSize =====

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void FormatFileSize_Returns_Human_Readable(long bytes, string expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.FormatFileSize(bytes));
    }

    // ===== IsPdfContentType =====

    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("image/jpeg", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsPdfContentType_Returns_Correct_Result(string? contentType, bool expected)
    {
        Assert.Equal(expected, AssetDisplayHelpers.IsPdfContentType(contentType));
    }
}
