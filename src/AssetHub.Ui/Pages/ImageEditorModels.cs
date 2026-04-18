namespace AssetHub.Ui.Pages;

public sealed class LayerInfo
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
}

public sealed class LayerProps
{
    public string? Id { get; set; }
    public string? Kind { get; set; }
    public string? Label { get; set; }
    public double? Opacity { get; set; }
    // Text
    public string? Text { get; set; }
    public int? FontSize { get; set; }
    public string? FontFamily { get; set; }
    public string? Fill { get; set; }
    public string? FontWeight { get; set; }
    public string? FontStyle { get; set; }
    public string? TextAlign { get; set; }
    // Shape
    public string? Stroke { get; set; }
    public int? StrokeWidth { get; set; }
    public int? Rx { get; set; }
    public int? Ry { get; set; }
}

internal sealed class CanvasSizeInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int FullResWidth { get; set; }
    public int FullResHeight { get; set; }
}
