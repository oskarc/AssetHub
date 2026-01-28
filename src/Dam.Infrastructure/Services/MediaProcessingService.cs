using Dam.Application.Repositories;
using Dam.Application.Services;
using Hangfire;
using Hangfire.Storage;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Dam.Infrastructure.Services;

public class MediaProcessingService(
    IAssetRepository assetRepository,
    IMinIOAdapter minioAdapter,
    IBackgroundJobClient backgroundJobClient,
    IOptions<MinIOSettings> minioSettings) : IMediaProcessingService
{
    private readonly string _bucketName = minioSettings.Value.BucketName;
    private const int ThumbWidth = 200;
    private const int ThumbHeight = 200;
    private const int MediumWidth = 800;
    private const int MediumHeight = 800;

    public async Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken cancellationToken = default)
    {
        string jobId;
        
        if (assetType == "image")
        {
            jobId = backgroundJobClient.Enqueue(() => ProcessImageAsync(assetId, originalObjectKey));
        }
        else if (assetType == "video")
        {
            jobId = backgroundJobClient.Enqueue(() => ProcessVideoAsync(assetId, originalObjectKey));
        }
        else
        {
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
        try
        {
            var asset = await assetRepository.GetByIdAsync(assetId);
            if (asset == null)
                return;

            // Download original image
            using var originalStream = await minioAdapter.DownloadAsync(_bucketName, originalObjectKey);
            var tempOriginal = Path.GetTempFileName();
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
            }
            catch (Exception ex)
            {
                // Log but don't fail - metadata extraction is optional
                asset.MetadataJson["metadataExtractionError"] = ex.Message;
            }

            // Create thumbnail
            var thumbPath = Path.GetTempFileName();
            await ResizeImageAsync(tempOriginal, thumbPath, ThumbWidth, ThumbHeight);
            var thumbKey = $"thumbs/{assetId}-thumb.jpg";
            using (var fs = File.OpenRead(thumbPath))
            {
                await minioAdapter.UploadAsync(_bucketName, thumbKey, fs, "image/jpeg");
            }

            // Create medium version
            var mediumPath = Path.GetTempFileName();
            await ResizeImageAsync(tempOriginal, mediumPath, MediumWidth, MediumHeight);
            var mediumKey = $"medium/{assetId}-medium.jpg";
            using (var fs = File.OpenRead(mediumPath))
            {
                await minioAdapter.UploadAsync(_bucketName, mediumKey, fs, "image/jpeg");
            }

            // Update asset with processed variants
            asset.MarkReady(thumbKey, mediumKey);
            await assetRepository.UpdateAsync(asset);

            // Cleanup
            File.Delete(tempOriginal);
            File.Delete(thumbPath);
            File.Delete(mediumPath);
        }
        catch (Exception ex)
        {
            var asset = await assetRepository.GetByIdAsync(assetId);
            if (asset != null)
            {
                asset.MarkFailed($"Image processing failed: {ex.Message}");
                await assetRepository.UpdateAsync(asset);
            }
        }
    }

    public async Task ProcessVideoAsync(Guid assetId, string originalObjectKey)
    {
        try
        {
            var asset = await assetRepository.GetByIdAsync(assetId);
            if (asset == null)
                return;

            // Download original video
            using var originalStream = await minioAdapter.DownloadAsync(_bucketName, originalObjectKey);
            var tempOriginal = Path.GetTempFileName();
            using (var fs = File.Create(tempOriginal))
            {
                await originalStream.CopyToAsync(fs);
            }

            // Extract poster frame at 5 seconds
            var posterPath = Path.GetTempFileName();
            await ExtractPosterAsync(tempOriginal, posterPath, 5);
            var posterKey = $"posters/{assetId}-poster.jpg";
            using (var fs = File.OpenRead(posterPath))
            {
                await minioAdapter.UploadAsync(_bucketName, posterKey, fs, "image/jpeg");
            }

            // Update asset with poster
            asset.MarkReady(posterKey: posterKey);
            await assetRepository.UpdateAsync(asset);

            // Cleanup
            File.Delete(tempOriginal);
            File.Delete(posterPath);
        }
        catch (Exception ex)
        {
            var asset = await assetRepository.GetByIdAsync(assetId);
            if (asset != null)
            {
                asset.MarkFailed($"Video processing failed: {ex.Message}");
                await assetRepository.UpdateAsync(asset);
            }
        }
    }

    public async Task<(bool IsCompleted, string? Status, string? ErrorMessage)> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        // This is a simplified implementation
        // In production, you'd use Hangfire's job storage API
        if (jobId == "no-processing-required")
        {
            return (true, "completed", null);
        }

        // Hangfire job tracking would be implemented here
        return (false, "processing", null);
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
