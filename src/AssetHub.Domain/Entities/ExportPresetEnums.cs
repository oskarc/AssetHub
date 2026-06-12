namespace AssetHub.Domain.Entities;

/// <summary>
/// How a preset resizes the source image to fit the target dimensions.
/// </summary>
public enum ExportPresetFitMode
{
    /// <summary>Scale to fit within the target box, preserving aspect ratio.</summary>
    Contain,
    /// <summary>Scale to cover the target box, cropping excess.</summary>
    Cover,
    /// <summary>Stretch to exact target dimensions, ignoring aspect ratio.</summary>
    Stretch,
    /// <summary>Scale to target width, height determined by aspect ratio.</summary>
    Width,
    /// <summary>Scale to target height, width determined by aspect ratio.</summary>
    Height
}

/// <summary>
/// Output image format for an export preset.
/// </summary>
public enum ExportPresetFormat
{
    /// <summary>Keep the same format as the source image.</summary>
    Original,
    Jpeg,
    Png,
    WebP
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the export preset enums.
/// </summary>
// Each enum's ToDbString is kept next to its parser instead of grouping all
// ToDbString overloads together — easier to scan per-enum. Suppress S4136 for the file.
#pragma warning disable S4136
public static class ExportPresetEnumExtensions
{
    public static string ToDbString(this ExportPresetFitMode mode) => mode switch
    {
        ExportPresetFitMode.Contain => "contain",
        ExportPresetFitMode.Cover => "cover",
        ExportPresetFitMode.Stretch => "stretch",
        ExportPresetFitMode.Width => "width",
        ExportPresetFitMode.Height => "height",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static ExportPresetFitMode ToExportPresetFitMode(this string value) => value switch
    {
        "contain" => ExportPresetFitMode.Contain,
        "cover" => ExportPresetFitMode.Cover,
        "stretch" => ExportPresetFitMode.Stretch,
        "width" => ExportPresetFitMode.Width,
        "height" => ExportPresetFitMode.Height,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown export preset fit mode: {value}")
    };

    private static readonly HashSet<string> ValidExportPresetFitModes =
        new(StringComparer.Ordinal) { "contain", "cover", "stretch", "width", "height" };

    public static bool IsValidExportPresetFitMode(string value) => ValidExportPresetFitModes.Contains(value);

    public static string ToDbString(this ExportPresetFormat format) => format switch
    {
        ExportPresetFormat.Original => "original",
        ExportPresetFormat.Jpeg => "jpeg",
        ExportPresetFormat.Png => "png",
        ExportPresetFormat.WebP => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static ExportPresetFormat ToExportPresetFormat(this string value) => value switch
    {
        "original" => ExportPresetFormat.Original,
        "jpeg" => ExportPresetFormat.Jpeg,
        "png" => ExportPresetFormat.Png,
        "webp" => ExportPresetFormat.WebP,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown export preset format: {value}")
    };

    private static readonly HashSet<string> ValidExportPresetFormats =
        new(StringComparer.Ordinal) { "original", "jpeg", "png", "webp" };

    public static bool IsValidExportPresetFormat(string value) => ValidExportPresetFormats.Contains(value);
}
#pragma warning restore S4136
