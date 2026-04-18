using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

public class ExportPresetDialogTests : BunitTestBase
{
    private async Task<IRenderedComponent<MudDialogProvider>> RenderCreateDialogAsync()
    {
        var parameters = new DialogParameters<ExportPresetDialog>();
        return await ShowDialogAsync<ExportPresetDialog>(parameters, "Create Preset");
    }

    private async Task<IRenderedComponent<MudDialogProvider>> RenderEditDialogAsync(ExportPresetDto? preset = null)
    {
        preset ??= TestData.CreateExportPreset(
            name: "Existing Preset",
            width: 1920,
            height: 1080,
            fitMode: "contain",
            format: "webp",
            quality: 90);

        var parameters = new DialogParameters<ExportPresetDialog>
        {
            { x => x.Preset, preset }
        };
        return await ShowDialogAsync<ExportPresetDialog>(parameters, "Edit Preset");
    }

    [Fact]
    public async Task Create_Mode_Shows_Create_Title()
    {
        var cut = await RenderCreateDialogAsync();

        Assert.Contains("ExportPresets_CreatePreset", cut.Markup);
    }

    [Fact]
    public async Task Edit_Mode_Shows_Edit_Title()
    {
        var cut = await RenderEditDialogAsync();

        Assert.Contains("ExportPresets_EditPreset", cut.Markup);
    }

    [Fact]
    public async Task Renders_Name_Field()
    {
        var cut = await RenderCreateDialogAsync();

        Assert.Contains("ExportPresets_Label_Name", cut.Markup);
    }

    [Fact]
    public async Task Renders_Width_And_Height_Fields()
    {
        var cut = await RenderCreateDialogAsync();

        Assert.Contains("ExportPresets_Label_Width", cut.Markup);
        Assert.Contains("ExportPresets_Label_Height", cut.Markup);
    }

    [Fact]
    public async Task Renders_FitMode_Select()
    {
        var cut = await RenderCreateDialogAsync();

        Assert.Contains("ExportPresets_Label_FitMode", cut.Markup);
    }

    [Fact]
    public async Task Renders_Format_Select()
    {
        var cut = await RenderCreateDialogAsync();

        Assert.Contains("ExportPresets_Label_Format", cut.Markup);
    }

    [Fact]
    public async Task Renders_Quality_Slider()
    {
        var cut = await RenderCreateDialogAsync();

        Assert.Contains("ExportPresets_Label_Quality", cut.Markup);
    }

    [Fact]
    public async Task Has_Cancel_And_Submit_Buttons()
    {
        var cut = await RenderCreateDialogAsync();

        Assert.Contains("Btn_Cancel", cut.Markup);
        Assert.Contains("ExportPresets_CreatePreset", cut.Markup);
    }

    [Fact]
    public async Task Edit_Mode_Shows_Save_Button()
    {
        var cut = await RenderEditDialogAsync();

        Assert.Contains("Btn_Save", cut.Markup);
    }

    [Fact]
    public async Task Edit_Mode_Prefills_Name()
    {
        var cut = await RenderEditDialogAsync();

        Assert.Contains("Existing Preset", cut.Markup);
    }

    [Fact]
    public async Task Edit_Mode_Prefills_Format()
    {
        var cut = await RenderEditDialogAsync();

        // webp is the preset format
        Assert.Contains("webp", cut.Markup);
    }

    [Fact]
    public async Task Renders_FitMode_Options()
    {
        var cut = await RenderCreateDialogAsync();

        // Check that the default fit mode (Contain) is rendered as the selected value
        Assert.Contains("ExportPresets_FitMode_Contain", cut.Markup);
    }

    [Fact]
    public async Task Shows_Default_Quality_85()
    {
        var cut = await RenderCreateDialogAsync();

        Assert.Contains("85%", cut.Markup);
    }

    [Fact]
    public async Task Edit_Mode_Shows_Preset_Quality()
    {
        var cut = await RenderEditDialogAsync();

        Assert.Contains("90%", cut.Markup);
    }
}
