using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Adapts <see cref="ImageProcessingService.ResizeForPresetAsync"/> to the
/// <see cref="IRenditionImageResizer"/> contract so <see cref="RenditionService"/>
/// can be unit-tested without ImageMagick. Synthesises an in-memory
/// <c>ExportPreset</c> from the request rather than introducing a
/// rendition-specific resize path — the preset pipeline is already battle-
/// tested via T1-VER-01 and the export-preset flow.
/// </summary>
public sealed class ImageProcessingRenditionResizer(ImageProcessingService inner) : IRenditionImageResizer
{
    public Task ResizeAsync(RenditionResizeRequest request, CancellationToken ct)
    {
        var preset = new ExportPreset
        {
            Id = Guid.Empty,
            Name = $"on-demand-{Guid.NewGuid():N}",
            Width = request.Width,
            Height = request.Height,
            FitMode = ParseFitMode(request.FitMode),
            Format = ParseFormat(request.Format),
            Quality = request.Quality
        };
        return inner.ResizeForPresetAsync(
            request.SourceObjectKey, request.TargetObjectKey, request.TargetContentType,
            preset, ct);
    }

    private static ExportPresetFitMode ParseFitMode(string fit) => fit.ToLowerInvariant() switch
    {
        "cover" => ExportPresetFitMode.Cover,
        _ => ExportPresetFitMode.Contain
    };

    private static ExportPresetFormat ParseFormat(string format) => format.ToLowerInvariant() switch
    {
        "png" => ExportPresetFormat.Png,
        "webp" => ExportPresetFormat.WebP,
        _ => ExportPresetFormat.Jpeg
    };
}
