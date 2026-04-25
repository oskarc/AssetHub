namespace AssetHub.Application.Services;

/// <summary>
/// On-the-fly image renditions (T3-REND-01). Validates params against
/// <see cref="Configuration.RenditionSettings"/>, checks MinIO for a
/// cached output (key derived deterministically from the params), and
/// either returns the cached presigned URL or generates the rendition
/// synchronously and caches it before returning.
/// </summary>
public interface IRenditionService
{
    /// <summary>
    /// Returns a presigned download URL for the rendition. Caller can
    /// either redirect the browser there or stream through the API.
    /// </summary>
    Task<ServiceResult<RenditionResult>> GetOrGenerateAsync(
        Guid assetId, RenditionRequest request, CancellationToken ct);
}

/// <summary>Validated rendition parameters.</summary>
public sealed record RenditionRequest(int? Width, int? Height, string FitMode, string Format);

/// <summary>Result returned by <see cref="IRenditionService.GetOrGenerateAsync"/>.</summary>
public sealed record RenditionResult(string Url, string ContentType, bool CacheHit);

/// <summary>
/// Single-method abstraction over the actual resize work. Production
/// wiring forwards to <c>ImageProcessingService.ResizeForPresetAsync</c>;
/// tests substitute a mock so they don't need ImageMagick on the box.
/// </summary>
public interface IRenditionImageResizer
{
    Task ResizeAsync(RenditionResizeRequest request, CancellationToken ct);
}

public sealed record RenditionResizeRequest(
    string SourceObjectKey,
    string TargetObjectKey,
    string TargetContentType,
    int? Width,
    int? Height,
    string FitMode,
    string Format,
    int Quality);
