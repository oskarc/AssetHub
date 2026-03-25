using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Processes image assets: creates thumbnail and medium-size renditions, extracts metadata.
/// Called by Hangfire background jobs.
/// </summary>
public sealed class ImageProcessingService(
    IAssetRepository assetRepository,
    IMinIOAdapter minioAdapter,
    IAuditService auditService,
    ImageMetadataExtractor metadataExtractor,
    IOptions<MinIOSettings> minioSettings,
    IOptions<ImageProcessingSettings> imageProcessingSettings,
    ILogger<ImageProcessingService> logger)
{
    private readonly string _bucketName = minioSettings.Value.BucketName;
    private readonly ImageProcessingSettings _imageSettings = imageProcessingSettings.Value;

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    public async Task ProcessImageAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default)
    {
        var tempOriginal = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var thumbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        var mediumPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");

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
                logger.LogInformation("Starting metadata extraction for asset {AssetId}, file: {FilePath}", assetId, tempOriginal);
                var metadata = metadataExtractor.ExtractImageMetadata(tempOriginal);
                foreach (var kvp in metadata)
                {
                    asset.MetadataJson[kvp.Key] = kvp.Value;
                }
                if (metadata.Count > 0)
                {
                    logger.LogInformation("Extracted {MetadataCount} metadata fields for asset {AssetId}: {MetadataKeys}",
                        metadata.Count, assetId, string.Join(", ", metadata.Keys));

                    // Auto-populate Copyright field from extracted metadata if not already set
                    if (string.IsNullOrWhiteSpace(asset.Copyright) &&
                        metadata.TryGetValue("copyright", out var extractedCopyright) &&
                        extractedCopyright is string copyrightStr &&
                        !string.IsNullOrWhiteSpace(copyrightStr))
                    {
                        asset.Copyright = copyrightStr;
                        logger.LogInformation("Auto-populated Copyright field for asset {AssetId} from EXIF: {Copyright}",
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

            await auditService.LogAsync(
                "asset.processing_failed",
                Constants.ScopeTypes.Asset,
                assetId,
                actorUserId: null,
                new Dictionary<string, object>
                {
                    ["assetType"] = "image",
                    ["error"] = ex.Message,
                    ["errorType"] = ex.GetType().Name
                },
                ct);

            var asset = await assetRepository.GetByIdAsync(assetId, ct);
            if (asset != null)
            {
                asset.MarkFailed("Image processing failed. Please try uploading again or contact an administrator.");
                await assetRepository.UpdateAsync(asset, ct);
            }
        }
        finally
        {
            CleanupTempFile(tempOriginal);
            CleanupTempFile(thumbPath);
            CleanupTempFile(mediumPath);
        }
    }

    private async Task ResizeImageAsync(string inputPath, string outputPath, int maxWidth, int maxHeight, CancellationToken ct = default)
    {
        var executable = OperatingSystem.IsWindows() ? "magick" : "/usr/bin/convert";
        var command = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        command.ArgumentList.Add($"{inputPath}[0]");
        command.ArgumentList.Add("-auto-orient");
        command.ArgumentList.Add("-background");
        command.ArgumentList.Add("white");
        command.ArgumentList.Add("-flatten");
        command.ArgumentList.Add("-colorspace");
        command.ArgumentList.Add("sRGB");
        command.ArgumentList.Add("-resize");
        command.ArgumentList.Add($"{maxWidth}x{maxHeight}>");
        command.ArgumentList.Add("-quality");
        command.ArgumentList.Add(_imageSettings.JpegQuality.ToString());
        command.ArgumentList.Add("-strip");
        command.ArgumentList.Add(outputPath);

        await RunProcessAsync(executable, command, ct);
    }

    private static async Task RunProcessAsync(string toolName, ProcessStartInfo startInfo, CancellationToken ct)
    {
        using var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException($"{toolName} process failed to start");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProcessTimeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{toolName} process exceeded the {ProcessTimeout.TotalMinutes:F0}-minute timeout and was killed");
        }

        await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{toolName} error (exit code {process.ExitCode}): {stderr}");
        }
    }

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
}
