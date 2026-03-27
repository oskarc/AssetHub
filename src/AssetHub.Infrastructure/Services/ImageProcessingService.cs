using System.Diagnostics;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [Queue("media-processing")]
    public async Task ProcessImageAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default)
    {
        var tempOriginal = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var thumbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        var mediumPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        var sw = Stopwatch.StartNew();

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

            // Create thumbnail and medium in a single ImageMagick invocation
            await CreateRenditionsAsync(tempOriginal, thumbPath, mediumPath, ct);

            // Upload thumbnail and medium in parallel
            var thumbKey = $"{Constants.StoragePrefixes.Thumbnails}/{assetId}-thumb.jpg";
            var mediumKey = $"{Constants.StoragePrefixes.Medium}/{assetId}-medium.jpg";

            await Task.WhenAll(
                UploadFileAsync(thumbPath, thumbKey, ct),
                UploadFileAsync(mediumPath, mediumKey, ct));

            // Update asset with processed variants
            asset.MarkReady(thumbKey, mediumKey);
            await assetRepository.UpdateAsync(asset, ct);

            sw.Stop();
            logger.LogInformation("Successfully processed image asset {AssetId} in {ElapsedMs}ms", assetId, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image processing failed for asset {AssetId} after {ElapsedMs}ms: {Error}", assetId, sw.ElapsedMilliseconds, ex.Message);

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

            throw; // Re-throw so Hangfire can retry
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
    /// Creates both thumbnail and medium renditions in a single ImageMagick call using +write.
    /// Reads the source image once, writes the thumbnail with +write, then continues to write the medium.
    /// </summary>
    private async Task CreateRenditionsAsync(string inputPath, string thumbPath, string mediumPath, CancellationToken ct)
    {
        var executable = OperatingSystem.IsWindows() ? "magick" : "/usr/bin/convert";
        var command = ProcessRunner.CreateStartInfo(executable);

        // Read source once, auto-orient and flatten
        command.ArgumentList.Add($"{inputPath}[0]");
        command.ArgumentList.Add("-auto-orient");
        command.ArgumentList.Add("-background");
        command.ArgumentList.Add("white");
        command.ArgumentList.Add("-flatten");
        command.ArgumentList.Add("-colorspace");
        command.ArgumentList.Add("sRGB");
        command.ArgumentList.Add("-strip");

        // Write thumbnail using +write (continues pipeline)
        command.ArgumentList.Add("(");
        command.ArgumentList.Add("+clone");
        command.ArgumentList.Add("-thumbnail");
        command.ArgumentList.Add($"{_imageSettings.ThumbnailWidth}x{_imageSettings.ThumbnailHeight}>");
        command.ArgumentList.Add("-quality");
        command.ArgumentList.Add(_imageSettings.JpegQuality.ToString());
        command.ArgumentList.Add("+write");
        command.ArgumentList.Add(thumbPath);
        command.ArgumentList.Add(")");

        // Write medium version
        command.ArgumentList.Add("-thumbnail");
        command.ArgumentList.Add($"{_imageSettings.MediumWidth}x{_imageSettings.MediumHeight}>");
        command.ArgumentList.Add("-quality");
        command.ArgumentList.Add(_imageSettings.JpegQuality.ToString());
        command.ArgumentList.Add(mediumPath);

        await ProcessRunner.RunAsync(executable, command, ProcessTimeout, logger, ct);
    }
}
