using System.Diagnostics;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Helpers;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Result of image processing — returned to the consumer for DB update and event publication.
/// </summary>
public sealed record ImageProcessingResult
{
    public bool Succeeded { get; init; }
    public string? ThumbObjectKey { get; init; }
    public string? MediumObjectKey { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public string? Copyright { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }

    public static ImageProcessingResult Success(
        string thumbKey, string mediumKey,
        Dictionary<string, object>? metadata, string? copyright)
        => new()
        {
            Succeeded = true,
            ThumbObjectKey = thumbKey,
            MediumObjectKey = mediumKey,
            Metadata = metadata,
            Copyright = copyright
        };

    public static ImageProcessingResult Failure(string message, string errorType)
        => new() { Succeeded = false, ErrorMessage = message, ErrorType = errorType };
}

/// <summary>
/// Processes image assets: creates thumbnail and medium-size renditions, extracts metadata.
/// Returns a result object — the caller (Wolverine handler) is responsible for DB updates.
/// </summary>
public sealed class ImageProcessingService(
    IMinIOAdapter minioAdapter,
    ImageMetadataExtractor metadataExtractor,
    IOptions<MinIOSettings> minioSettings,
    IOptions<ImageProcessingSettings> imageProcessingSettings,
    ILogger<ImageProcessingService> logger)
{
    private readonly string _bucketName = minioSettings.Value.BucketName;
    private readonly ImageProcessingSettings _imageSettings = imageProcessingSettings.Value;

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    public async Task<ImageProcessingResult> ProcessImageAsync(
        Guid assetId, string originalObjectKey, bool skipMetadata = false, CancellationToken ct = default)
    {
        var tempOriginal = ScratchPaths.Combine(Path.GetRandomFileName());
        var thumbPath = ScratchPaths.Combine($"{Guid.NewGuid()}.jpg");
        var mediumPath = ScratchPaths.Combine($"{Guid.NewGuid()}.jpg");
        var sw = Stopwatch.StartNew();

        try
        {
            logger.LogInformation("Starting image processing for asset {AssetId}", assetId);

            // Download original image
            using var originalStream = await minioAdapter.DownloadAsync(_bucketName, originalObjectKey, ct);
            using (var fs = File.Create(tempOriginal))
            {
                await originalStream.CopyToAsync(fs, ct);
            }

            // Extract EXIF metadata (skip for canvas-rendered edits — no EXIF in those PNGs)
            Dictionary<string, object> metadata = new();
            string? copyright = null;
            if (!skipMetadata)
            {
                try
                {
                    logger.LogInformation("Starting metadata extraction for asset {AssetId}, file: {FilePath}", assetId, tempOriginal);
                    metadata = metadataExtractor.ExtractImageMetadata(tempOriginal);
                    if (metadata.Count > 0)
                    {
                        logger.LogInformation("Extracted {MetadataCount} metadata fields for asset {AssetId}: {MetadataKeys}",
                            metadata.Count, assetId, string.Join(", ", metadata.Keys));

                        // Extract copyright from metadata if available
                        if (metadata.TryGetValue("copyright", out var extractedCopyright) &&
                            extractedCopyright is string copyrightStr &&
                            !string.IsNullOrWhiteSpace(copyrightStr))
                        {
                            copyright = copyrightStr;
                            logger.LogInformation("Found Copyright in EXIF for asset {AssetId}: {Copyright}",
                                assetId, copyrightStr);
                        }
                    }
                    else
                    {
                        logger.LogInformation("No metadata found in image for asset {AssetId}. File may not contain EXIF/IPTC data.", assetId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to extract metadata for asset {AssetId}: {ErrorMessage}", assetId, ex.Message);
                    metadata["metadataExtractionError"] = ex.Message;
                }
            }

            // Create thumbnail and medium in a single ImageMagick invocation
            await CreateRenditionsAsync(tempOriginal, thumbPath, mediumPath, ct);

            // Upload thumbnail and medium in parallel
            var thumbKey = $"{Constants.StoragePrefixes.Thumbnails}/{assetId}-thumb.jpg";
            var mediumKey = $"{Constants.StoragePrefixes.Medium}/{assetId}-medium.jpg";

            await Task.WhenAll(
                UploadFileAsync(thumbPath, thumbKey, ct),
                UploadFileAsync(mediumPath, mediumKey, ct));

            sw.Stop();
            logger.LogInformation("Successfully processed image asset {AssetId} in {ElapsedMs}ms", assetId, sw.ElapsedMilliseconds);

            return ImageProcessingResult.Success(thumbKey, mediumKey, metadata, copyright);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image processing failed for asset {AssetId} after {ElapsedMs}ms: {Error}", assetId, sw.ElapsedMilliseconds, ex.Message);
            return ImageProcessingResult.Failure(ex.Message, ex.GetType().Name);
        }
        finally
        {
            ProcessRunner.CleanupTempFile(tempOriginal, logger);
            ProcessRunner.CleanupTempFile(thumbPath, logger);
            ProcessRunner.CleanupTempFile(mediumPath, logger);
        }
    }

    private async Task UploadFileAsync(string localPath, string objectKey, CancellationToken ct)
    {
        using var fs = File.OpenRead(localPath);
        await minioAdapter.UploadAsync(_bucketName, objectKey, fs, Constants.ContentTypes.Jpeg, ct);
    }

    /// <summary>
    /// Creates both thumbnail and medium renditions using two separate ImageMagick calls.
    /// Runs sequentially to stay within the worker container's 1 GB memory limit —
    /// two concurrent ImageMagick processes on large images cause OOM (exit code 137).
    /// </summary>
    private async Task CreateRenditionsAsync(string inputPath, string thumbPath, string mediumPath, CancellationToken ct)
    {
        var executable = OperatingSystem.IsWindows() ? "magick" : "/usr/bin/convert";

        await CreateRenditionAsync(executable, inputPath, thumbPath,
            $"{_imageSettings.ThumbnailWidth}x{_imageSettings.ThumbnailHeight}>", ct);
        await CreateRenditionAsync(executable, inputPath, mediumPath,
            $"{_imageSettings.MediumWidth}x{_imageSettings.MediumHeight}>", ct);
    }

    private async Task CreateRenditionAsync(string executable, string inputPath, string outputPath, string geometry, CancellationToken ct)
    {
        var command = ProcessRunner.CreateStartInfo(executable);
        command.ArgumentList.Add($"{inputPath}[0]");
        command.ArgumentList.Add("-auto-orient");
        command.ArgumentList.Add("-background");
        command.ArgumentList.Add("white");
        command.ArgumentList.Add("-flatten");
        command.ArgumentList.Add("-colorspace");
        command.ArgumentList.Add("sRGB");
        command.ArgumentList.Add("-strip");
        command.ArgumentList.Add("-thumbnail");
        command.ArgumentList.Add(geometry);
        command.ArgumentList.Add("-quality");
        command.ArgumentList.Add(_imageSettings.JpegQuality.ToString());
        command.ArgumentList.Add(outputPath);

        await ProcessRunner.RunAsync(executable, command, ProcessTimeout, logger, ct);
    }

    /// <summary>
    /// Resize an image per export preset dimensions using ImageMagick.
    /// Downloads the source from MinIO, resizes to a temp file, uploads to the derivative key.
    /// </summary>
    /// <returns>The file size of the resized image in bytes.</returns>
    public async Task<long> ResizeForPresetAsync(
        string sourceObjectKey, string targetObjectKey, string targetContentType,
        ExportPreset preset, CancellationToken ct)
    {
        ValidatePresetDimensions(preset);

        var extension = targetContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };
        var tempInput = ScratchPaths.Combine(Path.GetRandomFileName());
        var tempOutput = ScratchPaths.Combine($"{Guid.NewGuid()}{extension}");

        try
        {
            await DownloadSourceToTempAsync(sourceObjectKey, tempInput, ct);
            await RunMagickResizeAsync(tempInput, tempOutput, preset, ct);

            var fileInfo = new FileInfo(tempOutput);
            using var outputStream = File.OpenRead(tempOutput);
            await minioAdapter.UploadAsync(_bucketName, targetObjectKey, outputStream, targetContentType, ct);

            return fileInfo.Length;
        }
        finally
        {
            ProcessRunner.CleanupTempFile(tempInput, logger);
            ProcessRunner.CleanupTempFile(tempOutput, logger);
        }
    }

    private static void ValidatePresetDimensions(ExportPreset preset)
    {
        // Defense-in-depth: validate even though presets are validated at creation.
        const int maxDimension = 10000;
        if (preset.Width.HasValue && (preset.Width.Value < 1 || preset.Width.Value > maxDimension))
            throw new ArgumentOutOfRangeException(nameof(preset), $"Width must be between 1 and {maxDimension}");
        if (preset.Height.HasValue && (preset.Height.Value < 1 || preset.Height.Value > maxDimension))
            throw new ArgumentOutOfRangeException(nameof(preset), $"Height must be between 1 and {maxDimension}");
        if (preset.Quality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(preset), "Quality must be between 1 and 100");
    }

    private async Task DownloadSourceToTempAsync(string sourceObjectKey, string tempInput, CancellationToken ct)
    {
        using var sourceStream = await minioAdapter.DownloadAsync(_bucketName, sourceObjectKey, ct);
        await using var fs = File.Create(tempInput);
        await sourceStream.CopyToAsync(fs, ct);
    }

    private async Task RunMagickResizeAsync(
        string tempInput, string tempOutput, ExportPreset preset, CancellationToken ct)
    {
        var executable = OperatingSystem.IsWindows() ? "magick" : "/usr/bin/convert";
        var command = ProcessRunner.CreateStartInfo(executable);
        command.ArgumentList.Add($"{tempInput}[0]");
        command.ArgumentList.Add("-auto-orient");
        command.ArgumentList.Add("-colorspace");
        command.ArgumentList.Add("sRGB");
        command.ArgumentList.Add("-strip");

        AppendGeometryArgs(command, preset);

        command.ArgumentList.Add("-quality");
        command.ArgumentList.Add(preset.Quality.ToString());
        command.ArgumentList.Add(tempOutput);

        await ProcessRunner.RunAsync(executable, command, ProcessTimeout, logger, ct);
    }

    private static void AppendGeometryArgs(System.Diagnostics.ProcessStartInfo command, ExportPreset preset)
    {
        var geometry = BuildPresetGeometry(preset.Width, preset.Height, preset.FitMode);
        if (geometry is null) return;

        command.ArgumentList.Add("-resize");
        command.ArgumentList.Add(geometry);

        if (preset.FitMode == ExportPresetFitMode.Cover && preset.Width.HasValue && preset.Height.HasValue)
        {
            command.ArgumentList.Add("-gravity");
            command.ArgumentList.Add("center");
            command.ArgumentList.Add("-extent");
            command.ArgumentList.Add($"{preset.Width.Value}x{preset.Height.Value}");
        }
    }

    private static string? BuildPresetGeometry(int? width, int? height, ExportPresetFitMode fitMode) => fitMode switch
    {
        ExportPresetFitMode.Contain when width.HasValue && height.HasValue => $"{width.Value}x{height.Value}",
        ExportPresetFitMode.Cover when width.HasValue && height.HasValue => $"{width.Value}x{height.Value}^",
        ExportPresetFitMode.Stretch when width.HasValue && height.HasValue => $"{width.Value}x{height.Value}!",
        ExportPresetFitMode.Width when width.HasValue => $"{width.Value}x",
        ExportPresetFitMode.Height when height.HasValue => $"x{height.Value}",
        _ => null
    };
}