using Dam.Application;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Hangfire;
using Hangfire.Storage;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Dam.Infrastructure.Services;

public class MediaProcessingService(
    IAssetRepository assetRepository,
    IMinIOAdapter minioAdapter,
    IBackgroundJobClient backgroundJobClient,
    IOptions<MinIOSettings> minioSettings,
    ILogger<MediaProcessingService> logger) : IMediaProcessingService
{
    private readonly string _bucketName = minioSettings.Value.BucketName;

    public async Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken cancellationToken = default)
    {
        string jobId;
        
        if (assetType == "image")
        {
            logger.LogInformation("Scheduling image processing for asset {AssetId}", assetId);
            jobId = backgroundJobClient.Enqueue(() => ProcessImageAsync(assetId, originalObjectKey));
        }
        else if (assetType == "video")
        {
            logger.LogInformation("Scheduling video processing for asset {AssetId}", assetId);
            jobId = backgroundJobClient.Enqueue(() => ProcessVideoAsync(assetId, originalObjectKey));
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

    public async Task ProcessImageAsync(Guid assetId, string originalObjectKey)
    {
        var tempOriginal = Path.GetTempFileName();
        var thumbPath = Path.GetTempFileName();
        var mediumPath = Path.GetTempFileName();
        
        try
        {
            logger.LogInformation("Starting image processing for asset {AssetId}", assetId);
            
            var asset = await assetRepository.GetByIdAsync(assetId);
            if (asset == null)
            {
                logger.LogWarning("Asset {AssetId} not found, skipping processing", assetId);
                return;
            }

            // Download original image
            using var originalStream = await minioAdapter.DownloadAsync(_bucketName, originalObjectKey);
            using (var fs = File.Create(tempOriginal))
            {
                await originalStream.CopyToAsync(fs);
            }

            // Extract EXIF metadata
            try
            {
                var metadata = ExtractImageMetadata(tempOriginal);
                foreach (var kvp in metadata)
                {
                    asset.MetadataJson[kvp.Key] = kvp.Value;
                }
                logger.LogDebug("Extracted {Count} metadata fields for asset {AssetId}", metadata.Count, assetId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to extract metadata for asset {AssetId}", assetId);
                asset.MetadataJson["metadataExtractionError"] = ex.Message;
            }

            // Create thumbnail
            await ResizeImageAsync(tempOriginal, thumbPath, Constants.ImageDimensions.ThumbnailWidth, Constants.ImageDimensions.ThumbnailHeight);
            var thumbKey = $"{Constants.StoragePrefixes.Thumbnails}/{assetId}-thumb.jpg";
            using (var fs = File.OpenRead(thumbPath))
            {
                await minioAdapter.UploadAsync(_bucketName, thumbKey, fs, Constants.ContentTypes.Jpeg);
            }

            // Create medium version
            await ResizeImageAsync(tempOriginal, mediumPath, Constants.ImageDimensions.MediumWidth, Constants.ImageDimensions.MediumHeight);
            var mediumKey = $"{Constants.StoragePrefixes.Medium}/{assetId}-medium.jpg";
            using (var fs = File.OpenRead(mediumPath))
            {
                await minioAdapter.UploadAsync(_bucketName, mediumKey, fs, Constants.ContentTypes.Jpeg);
            }

            // Update asset with processed variants
            asset.MarkReady(thumbKey, mediumKey);
            await assetRepository.UpdateAsync(asset);
            
            logger.LogInformation("Successfully processed image asset {AssetId}", assetId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image processing failed for asset {AssetId}", assetId);
            
            var asset = await assetRepository.GetByIdAsync(assetId);
            if (asset != null)
            {
                asset.MarkFailed($"Image processing failed: {ex.Message}");
                await assetRepository.UpdateAsync(asset);
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

    public async Task ProcessVideoAsync(Guid assetId, string originalObjectKey)
    {
        var tempOriginal = Path.GetTempFileName();
        var posterPath = Path.GetTempFileName();
        
        try
        {
            logger.LogInformation("Starting video processing for asset {AssetId}", assetId);
            
            var asset = await assetRepository.GetByIdAsync(assetId);
            if (asset == null)
            {
                logger.LogWarning("Asset {AssetId} not found, skipping processing", assetId);
                return;
            }

            // Download original video
            using var originalStream = await minioAdapter.DownloadAsync(_bucketName, originalObjectKey);
            using (var fs = File.Create(tempOriginal))
            {
                await originalStream.CopyToAsync(fs);
            }

            // Extract poster frame at 5 seconds
            await ExtractPosterAsync(tempOriginal, posterPath, 5);
            var posterKey = $"{Constants.StoragePrefixes.Posters}/{assetId}-poster.jpg";
            using (var fs = File.OpenRead(posterPath))
            {
                await minioAdapter.UploadAsync(_bucketName, posterKey, fs, Constants.ContentTypes.Jpeg);
            }

            // Update asset with poster
            asset.MarkReady(posterKey: posterKey);
            await assetRepository.UpdateAsync(asset);
            
            logger.LogInformation("Successfully processed video asset {AssetId}", assetId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Video processing failed for asset {AssetId}", assetId);
            
            var asset = await assetRepository.GetByIdAsync(assetId);
            if (asset != null)
            {
                asset.MarkFailed($"Video processing failed: {ex.Message}");
                await assetRepository.UpdateAsync(asset);
            }
        }
        finally
        {
            // Cleanup temp files
            CleanupTempFile(tempOriginal);
            CleanupTempFile(posterPath);
        }
    }

    public async Task<(bool IsCompleted, string? Status, string? ErrorMessage)> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (jobId == "no-processing-required")
        {
            return (true, "completed", null);
        }

        // Hangfire job tracking would be implemented here
        return (false, "processing", null);
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
    /// Resize image using ImageMagick command-line tool.
    /// Assumes 'magick' or 'convert' is in PATH.
    /// </summary>
    private async Task ResizeImageAsync(string inputPath, string outputPath, int maxWidth, int maxHeight)
    {
        var command = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("magick", $"\"{inputPath}\" -resize {maxWidth}x{maxHeight} -quality 85 \"{outputPath}\"")
            : new ProcessStartInfo("convert", $"\"{inputPath}\" -resize {maxWidth}x{maxHeight} -quality 85 \"{outputPath}\"");

        command.RedirectStandardOutput = true;
        command.RedirectStandardError = true;
        command.UseShellExecute = false;
        command.CreateNoWindow = true;

        using var process = Process.Start(command);
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"ImageMagick error: {error}");
            }
        }
    }

    /// <summary>
    /// Extract poster frame from video at specified second using ffmpeg.
    /// </summary>
    private async Task ExtractPosterAsync(string inputPath, string outputPath, int atSecond)
    {
        var command = new ProcessStartInfo("ffmpeg",
            $"-ss {atSecond} -i \"{inputPath}\" -vframes 1 -vf \"scale=800:-1\" -q:v 5 \"{outputPath}\" -y")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(command);
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"FFmpeg error: {error}");
            }
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

            // Key EXIF fields we want to extract
            var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();

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

            // IPTC fields (often contain credits/captions)
            if (iptc != null)
            {
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

            // GPS coordinates (if available)
            var gps = directories.OfType<MetadataExtractor.Formats.Exif.GpsDirectory>().FirstOrDefault();
            if (gps != null)
            {
                var location = gps.GetGeoLocation();
                if (location != null)
                {
                    result["gpsLatitude"] = location.Latitude;
                    result["gpsLongitude"] = location.Longitude;
                }
            }
        }
        catch
        {
            // Silently ignore metadata extraction errors - file may not have EXIF
        }

        return result;
    }
}
