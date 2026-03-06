namespace AssetHub.Application.Configuration;

/// <summary>
/// Configuration settings for image and video processing.
/// Bound to the "ImageProcessing" section in appsettings.
/// </summary>
public class ImageProcessingSettings
{
    public const string SectionName = "ImageProcessing";

    /// <summary>
    /// Width of generated thumbnails in pixels.
    /// </summary>
    public int ThumbnailWidth { get; set; } = 200;

    /// <summary>
    /// Height of generated thumbnails in pixels.
    /// </summary>
    public int ThumbnailHeight { get; set; } = 200;

    /// <summary>
    /// Width of medium-sized image variants in pixels.
    /// </summary>
    public int MediumWidth { get; set; } = 800;

    /// <summary>
    /// Height of medium-sized image variants in pixels.
    /// </summary>
    public int MediumHeight { get; set; } = 800;

    /// <summary>
    /// JPEG quality (1-100) for processed images.
    /// </summary>
    public int JpegQuality { get; set; } = 85;

    /// <summary>
    /// Timestamp in seconds at which to extract the video poster frame.
    /// </summary>
    public int PosterFrameSeconds { get; set; } = 5;

    /// <summary>
    /// Width in pixels for the video poster image.
    /// </summary>
    public int PosterWidth { get; set; } = 800;

    /// <summary>
    /// Quality parameter for the video poster image (ffmpeg -q:v scale, lower = better).
    /// </summary>
    public int PosterQuality { get; set; } = 5;

    /// <summary>
    /// When false (default), GPS coordinates are not extracted from EXIF metadata,
    /// preventing unintentional storage of the location where a photo was taken
    /// (GDPR / privacy concern). Set to true only if GPS coordinates are required
    /// for your use case.
    /// </summary>
    public bool IncludeGpsData { get; set; } = false;
}
