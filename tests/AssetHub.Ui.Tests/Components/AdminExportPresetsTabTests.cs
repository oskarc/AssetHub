using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

public class AdminExportPresetsTabTests : BunitTestBase
{
    private List<ExportPresetDto> _defaultPresets = null!;

    private void SetupPresets(List<ExportPresetDto>? presets = null)
    {
        _defaultPresets = presets ?? new List<ExportPresetDto>
        {
            TestData.CreateExportPreset(name: "Web Large", width: 1920, height: 1080, format: "jpeg", quality: 85),
            TestData.CreateExportPreset(name: "Thumbnail", width: 300, height: 300, format: "webp", quality: 75, fitMode: "contain")
        };

        MockApi.Setup(a => a.GetExportPresetsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_defaultPresets);
    }

    private IRenderedComponent<AdminExportPresetsTab> RenderTab()
    {
        return Render<AdminExportPresetsTab>();
    }

    [Fact]
    public void Shows_Loading_Indicator_Initially()
    {
        // Don't setup mock — API call won't complete
        MockApi.Setup(a => a.GetExportPresetsAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<List<ExportPresetDto>>().Task);

        var cut = RenderTab();

        Assert.Contains("mud-progress-linear", cut.Markup);
    }

    [Fact]
    public void Shows_Empty_State_When_No_Presets()
    {
        SetupPresets([]);

        var cut = RenderTab();

        Assert.Contains("ExportPresets_NoPresetsYet", cut.Markup);
        Assert.Contains("ExportPresets_NoPresetsDesc", cut.Markup);
    }

    [Fact]
    public void Empty_State_Has_Create_Button()
    {
        SetupPresets([]);

        var cut = RenderTab();

        Assert.Contains("ExportPresets_CreatePreset", cut.Markup);
    }

    [Fact]
    public void Renders_Table_With_Presets()
    {
        SetupPresets();

        var cut = RenderTab();

        Assert.Contains("Web Large", cut.Markup);
        Assert.Contains("Thumbnail", cut.Markup);
    }

    [Fact]
    public void Renders_Table_Headers()
    {
        SetupPresets();

        var cut = RenderTab();

        Assert.Contains("ExportPresets_Header_Name", cut.Markup);
        Assert.Contains("ExportPresets_Header_Dimensions", cut.Markup);
        Assert.Contains("ExportPresets_Header_FitMode", cut.Markup);
        Assert.Contains("ExportPresets_Header_Format", cut.Markup);
        Assert.Contains("ExportPresets_Header_Quality", cut.Markup);
        Assert.Contains("ExportPresets_Header_Created", cut.Markup);
    }

    [Fact]
    public void Renders_Preset_Dimensions()
    {
        SetupPresets(new List<ExportPresetDto>
        {
            TestData.CreateExportPreset(name: "Custom", width: 800, height: 600)
        });

        var cut = RenderTab();

        Assert.Contains("800", cut.Markup);
        Assert.Contains("600", cut.Markup);
    }

    [Fact]
    public void Renders_Any_For_Null_Dimensions()
    {
        SetupPresets(new List<ExportPresetDto>
        {
            TestData.CreateExportPreset(name: "Flexible", width: null, height: 600)
        });

        var cut = RenderTab();

        Assert.Contains("ExportPresets_DimensionsAny", cut.Markup);
    }

    [Fact]
    public void Renders_Format_Chip()
    {
        SetupPresets(new List<ExportPresetDto>
        {
            TestData.CreateExportPreset(name: "WebP Preset", format: "webp")
        });

        var cut = RenderTab();

        Assert.Contains("WEBP", cut.Markup);
    }

    [Fact]
    public void Renders_Quality_Percentage()
    {
        SetupPresets(new List<ExportPresetDto>
        {
            TestData.CreateExportPreset(name: "HQ", quality: 95)
        });

        var cut = RenderTab();

        Assert.Contains("95%", cut.Markup);
    }

    [Fact]
    public void Renders_FitMode_Localized()
    {
        SetupPresets(new List<ExportPresetDto>
        {
            TestData.CreateExportPreset(name: "Cover", fitMode: "cover")
        });

        var cut = RenderTab();

        Assert.Contains("ExportPresets_FitMode_Cover", cut.Markup);
    }

    [Fact]
    public void Has_Edit_And_Delete_Buttons_Per_Row()
    {
        SetupPresets(new List<ExportPresetDto>
        {
            TestData.CreateExportPreset(name: "Single")
        });

        var cut = RenderTab();

        Assert.Contains("ExportPresets_EditPreset", cut.Markup);
        Assert.Contains("ExportPresets_DeletePreset", cut.Markup);
    }

    [Fact]
    public void Has_Create_Button_In_Toolbar()
    {
        SetupPresets();

        var cut = RenderTab();

        Assert.Contains("ExportPresets_CreatePreset", cut.Markup);
    }

    [Fact]
    public void Has_Description_Text()
    {
        SetupPresets();

        var cut = RenderTab();

        Assert.Contains("ExportPresets_Desc", cut.Markup);
    }

    [Fact]
    public void Calls_Api_On_Init()
    {
        SetupPresets();

        RenderTab();

        MockApi.Verify(a => a.GetExportPresetsAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public void Handles_Api_Error_Gracefully()
    {
        MockApi.Setup(a => a.GetExportPresetsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API error"));

        RenderTab();

        // Should show empty state (presets is null) and handle error
        VerifyHandleErrorCalled();
    }
}
