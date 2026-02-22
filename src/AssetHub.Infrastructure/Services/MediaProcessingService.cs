using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using Hangfire;
using Hangfire.Storage;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace AssetHub.Infrastructure.Services;

public class MediaProcessingService(
    IAssetRepository assetRepository,
    IMinIOAdapter minioAdapter,
    IBackgroundJobClient backgroundJobClient,
    IOptions<MinIOSettings> minioSettings,
    IOptions<ImageProcessingSettings> imageProcessingSettings,
    ILogger<MediaProcessingService> logger) : IMediaProcessingService
{
    private readonly string _bucketName = minioSettings.Value.BucketName;
    private readonly ImageProcessingSettings _imageSettings = imageProcessingSettings.Value;

    public async Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken cancellationToken = default)
    {
        string jobId;
        
        if (assetType == Constants.AssetTypeFilters.Image)
        {
            logger.LogInformation("Scheduling image processing for asset {AssetId}", assetId);
            jobId = backgroundJobClient.Enqueue(() => ProcessImageAsync(assetId, originalObjectKey, CancellationToken.None));
        }
        else if (assetType == Constants.AssetTypeFilters.Video)
        {
            logger.LogInformation("Scheduling video processing for asset {AssetId}", assetId);
            jobId = backgroundJobClient.Enqueue(() => ProcessVideoAsync(assetId, originalObjectKey, CancellationToken.None));
        }
        else
        {
            logger.LogInformation("No processing required for asset {AssetId} of type {AssetType}", assetId, assetType);
            // For documents and other types, mark as ready immediately
            var asset = await assetRepository.GetByIdAsync(assetId, cancellationToken);
            if (asset != null)
            {
                asset.MarkReady();
                await assetRepository.UpdateAsync(asset, cancellationToken);
            }
            jobId = "no-processing-required";
        }

        return jobId;
    }

    public async Task ProcessImageAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default)
    {
        var tempOriginal = Path.GetTempFileName();
        var thumbPath = Path.GetTempFileName();
        var mediumPath = Path.GetTempFileName();
        
        try
        {
            logger.LogInformation("Starting image processing for asset {AssetId}", assetId);
            
            var asset = await assetRepository.GetByIdAsync(assetId, ct);
            if (asset == null)
            {
                logger.LogWarning("Asset {AssetId} not found, skipping processing", assetId);
                return;
            }

            // Download original image
            using var originalStream = await minioAdapter.DownloadAsync(_bucketName, originalObjectKey, ct);
            using (var fs = File.Create(tempOriginal))
            {
                await originalStream.CopyToAsync(fs, ct);
            }

            // Extract EXIF metadata
            try
            {
                var metadata = ExtractImageMetadata(tempOriginal);
                foreach (var kvp in metadata)
                {
                    asset.MetadataJson[kvp.Key] = kvp.Value;
                }
                logger.LogDebug("Extracted {MetadataCount} metadata fields for asset {AssetId}", metadata.Count, assetId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to extract metadata for asset {AssetId}", assetId);
                asset.MetadataJson["metadataExtractionError"] = ex.Message;
            }

            // Create thumbnail
            await ResizeImageAsync(tempOriginal, thumbPath, _imageSettings.ThumbnailWidth, _imageSettings.ThumbnailHeight, ct);
            var thumbKey = $"{Constants.StoragePrefixes.Thumbnails}/{assetId}-thumb.jpg";
            using (var fs = File.OpenRead(thumbPath))
            {
                await minioAdapter.UploadAsync(_bucketName, thumbKey, fs, Constants.ContentTypes.Jpeg, ct);
            }

            // Create medium version
            await ResizeImageAsync(tempOriginal, mediumPath, _imageSettings.MediumWidth, _imageSettings.MediumHeight, ct);
            var mediumKey = $"{Constants.StoragePrefixes.Medium}/{assetId}-medium.jpg";
            using (var fs = File.OpenRead(mediumPath))
            {
                await minioAdapter.UploadAsync(_bucketName, mediumKey, fs, Constants.ContentTypes.Jpeg, ct);
            }

            // Update asset with processed variants
            asset.MarkReady(thumbKey, mediumKey);
            await assetRepository.UpdateAsync(asset, ct);
            
            logger.LogInformation("Successfully processed image asset {AssetId}", assetId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image processing failed for asset {AssetId}: {Error}", assetId, ex.Message);
            
            var asset = await assetRepository.GetByIdAsync(assetId, ct);
            if (asset != null)
            {
                asset.MarkFailed("Image processing failed. Please try uploading again or contact an administrator.");
                await assetRepository.UpdateAsync(asset, ct);
            }
        }
        finally
        {
            // Cleanup temp files
            CleanupTempFile(tempOriginal);
            CleanupTempFile(thumbPath);
            CleanupTempFile(mediumPath);
        }
    }

    public async Task ProcessVideoAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default)
    {
        var tempOriginal = Path.GetTempFileName();
        var posterPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        
        try
        {
            logger.LogInformation("Starting video processing for asset {AssetId}", assetId);
            
            var asset = await assetRepository.GetByIdAsync(assetId, ct);
            if (asset == null)
            {
                logger.LogWarning("Asset {AssetId} not found, skipping processing", assetId);
                return;
            }

            // Download original video
            using var originalStream = await minioAdapter.DownloadAsync(_bucketName, originalObjectKey, ct);
            using (var fs = File.Create(tempOriginal))
            {
                await originalStream.CopyToAsync(fs, ct);
            }

            // Extract poster frame
            await ExtractPosterAsync(tempOriginal, posterPath, _imageSettings.PosterFrameSeconds, ct);
            var posterKey = $"{Constants.StoragePrefixes.Posters}/{assetId}-poster.jpg";
            using (var fs = File.OpenRead(posterPath))
            {
                await minioAdapter.UploadAsync(_bucketName, posterKey, fs, Constants.ContentTypes.Jpeg, ct);
            }

            // Update asset with poster
            asset.MarkReady(posterKey: posterKey);
            await assetRepository.UpdateAsync(asset, ct);
            
            logger.LogInformation("Successfully processed video asset {AssetId}", assetId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Video processing failed for asset {AssetId}: {Error}", assetId, ex.Message);
            
            var asset = await assetRepository.GetByIdAsync(assetId, ct);
            if (asset != null)
            {
                asset.MarkFailed("Video processing failed. Please try uploading again or contact an administrator.");
                await assetRepository.UpdateAsync(asset, ct);
            }
        }
        finally
        {
            // Cleanup temp files
            CleanupTempFile(tempOriginal);
            CleanupTempFile(posterPath);
        }
    }

    /// <summary>
    /// Safely delete a temp file, logging any errors.
    /// </summary>
    private void CleanupTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cleanup temp file: {Path}", path);
        }
    }

    /// <summary>
    /// Maximum time to wait for an external process (ImageMagick / FFmpeg) before killing it.
    /// </summary>
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Resize image using ImageMagick command-line tool.
    /// Assumes 'magick' or 'convert' is in PATH.
    /// </summary>
    private async Task ResizeImageAsync(string inputPath, string outputPath, int maxWidth, int maxHeight, CancellationToken ct = default)
    {
        var executable = OperatingSystem.IsWindows() ? "magick" : "convert";
        var command = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        command.ArgumentList.Add(inputPath);
        command.ArgumentList.Add("-resize");
        command.ArgumentList.Add($"{maxWidth}x{maxHeight}>");
        command.ArgumentList.Add("-quality");
        command.ArgumentList.Add(_imageSettings.JpegQuality.ToString());
        command.ArgumentList.Add(outputPath);

        await RunProcessAsync(executable, command, ct);
    }

    /// <summary>
    /// Extract poster frame from video at specified second using ffmpeg.
    /// </summary>
    private async Task ExtractPosterAsync(string inputPath, string outputPath, int atSecond, CancellationToken ct = default)
    {
        var command = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        command.ArgumentList.Add("-ss");
        command.ArgumentList.Add(atSecond.ToString());
        command.ArgumentList.Add("-i");
        command.ArgumentList.Add(inputPath);
        command.ArgumentList.Add("-vframes");
        command.ArgumentList.Add("1");
        command.ArgumentList.Add("-vf");
        command.ArgumentList.Add($"scale={_imageSettings.PosterWidth}:-1");
        command.ArgumentList.Add("-q:v");
        command.ArgumentList.Add(_imageSettings.PosterQuality.ToString());
        command.ArgumentList.Add(outputPath);
        command.ArgumentList.Add("-y");

        await RunProcessAsync("ffmpeg", command, ct);
    }

    /// <summary>
    /// Runs an external process with a timeout guard.
    /// </summary>
    private async Task RunProcessAsync(string toolName, ProcessStartInfo startInfo, CancellationToken ct)
    {
        using var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException($"{toolName} process failed to start");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProcessTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{toolName} process exceeded the {ProcessTimeout.TotalMinutes:F0}-minute timeout and was killed");
        }

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"{toolName} error (exit code {process.ExitCode}): {error}");
        }
    }

    /// <summary>
    /// Extract EXIF/IPTC/XMP metadata from an image file.
    /// </summary>
    private Dictionary<string, object> ExtractImageMetadata(string imagePath)
    {
        var result = new Dictionary<string, object>();

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(imagePath);

            ExtractExifData(directories, result);
            ExtractIptcData(directories, result);
            ExtractGpsData(directories, result);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Metadata extraction is non-critical — file may not have EXIF data
            logger.LogDebug(ex, "Metadata extraction returned no data for {ImagePath}", imagePath);
        }

        return result;
    }

    private static void ExtractExifData(IReadOnlyList<MetadataExtractor.Directory> directories, Dictionary<string, object> result)
    {
        var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

        // Artist / Photographer
        var artist = exifIfd0?.GetString(ExifDirectoryBase.TagArtist);
        if (!string.IsNullOrWhiteSpace(artist))
            result["artist"] = artist;

        // Copyright
        var copyright = exifIfd0?.GetString(ExifDirectoryBase.TagCopyright);
        if (!string.IsNullOrWhiteSpace(copyright))
            result["copyright"] = copyright;

        // Camera Make
        var make = exifIfd0?.GetString(ExifDirectoryBase.TagMake);
        if (!string.IsNullOrWhiteSpace(make))
            result["cameraMake"] = make;

        // Camera Model
        var model = exifIfd0?.GetString(ExifDirectoryBase.TagModel);
        if (!string.IsNullOrWhiteSpace(model))
            result["cameraModel"] = model;

        // Software used
        var software = exifIfd0?.GetString(ExifDirectoryBase.TagSoftware);
        if (!string.IsNullOrWhiteSpace(software))
            result["software"] = software;

        // Date/Time Original
        if (exifSubIfd?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTimeOriginal) == true)
            result["dateTaken"] = dateTimeOriginal.ToString("yyyy-MM-dd HH:mm:ss");

        // Exposure Time
        if (exifSubIfd?.TryGetRational(ExifDirectoryBase.TagExposureTime, out var exposureTime) == true)
            result["exposureTime"] = exposureTime.Denominator > 1 
                ? $"1/{(int)(exposureTime.Denominator / exposureTime.Numerator)}s" 
                : $"{exposureTime.ToDouble():F1}s";

        // F-Number (Aperture)
        if (exifSubIfd?.TryGetRational(ExifDirectoryBase.TagFNumber, out var fNumber) == true)
            result["aperture"] = $"f/{fNumber.ToDouble():F1}";

        // ISO
        if (exifSubIfd?.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso) == true)
            result["iso"] = iso;

        // Focal Length
        if (exifSubIfd?.TryGetRational(ExifDirectoryBase.TagFocalLength, out var focalLength) == true)
            result["focalLength"] = $"{focalLength.ToDouble():F0}mm";

        // Image Dimensions
        if (exifSubIfd?.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var width) == true)
            result["imageWidth"] = width;
        if (exifSubIfd?.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var height) == true)
            result["imageHeight"] = height;

        // Flash
        if (exifSubIfd?.TryGetInt32(ExifDirectoryBase.TagFlash, out var flash) == true)
            result["flash"] = (flash & 1) == 1 ? "Fired" : "Did not fire";
    }

    private static void ExtractIptcData(IReadOnlyList<MetadataExtractor.Directory> directories, Dictionary<string, object> result)
    {
        var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();
        if (iptc == null) return;

        var byLine = iptc.GetString(IptcDirectory.TagByLine);
        if (!string.IsNullOrWhiteSpace(byLine) && !result.ContainsKey("artist"))
            result["artist"] = byLine;

        var credit = iptc.GetString(IptcDirectory.TagCredit);
        if (!string.IsNullOrWhiteSpace(credit))
            result["credit"] = credit;

        var caption = iptc.GetString(IptcDirectory.TagCaption);
        if (!string.IsNullOrWhiteSpace(caption))
            result["caption"] = caption;

        var source = iptc.GetString(IptcDirectory.TagSource);
        if (!string.IsNullOrWhiteSpace(source))
            result["source"] = source;

        var keywords = iptc.GetStringArray(IptcDirectory.TagKeywords);
        if (keywords != null && keywords.Length > 0)
            result["keywords"] = string.Join(", ", keywords);
    }

    private static void ExtractGpsData(IReadOnlyList<MetadataExtractor.Directory> directories, Dictionary<string, object> result)
    {
        var gps = directories.OfType<MetadataExtractor.Formats.Exif.GpsDirectory>().FirstOrDefault();
        if (gps == null) return;

        var location = gps.GetGeoLocation();
        if (location != null)
        {
            result["gpsLatitude"] = location.Latitude;
            result["gpsLongitude"] = location.Longitude;
        }
    }
}
