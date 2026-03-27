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
/// Processes video assets: extracts a poster frame and thumbnail.
/// Uses presigned URLs so FFmpeg can stream directly from MinIO without downloading the entire file.
/// Called by Hangfire background jobs.
/// </summary>
public sealed class VideoProcessingService(
    IAssetRepository assetRepository,
    IMinIOAdapter minioAdapter,
    IAuditService auditService,
    IOptions<MinIOSettings> minioSettings,
    IOptions<ImageProcessingSettings> imageProcessingSettings,
    ILogger<VideoProcessingService> logger)
{
    private readonly string _bucketName = minioSettings.Value.BucketName;
    private readonly ImageProcessingSettings _imageSettings = imageProcessingSettings.Value;

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [Queue("media-processing")]
    public async Task ProcessVideoAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default)
    {
        var posterPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        var thumbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        var sw = Stopwatch.StartNew();

        try
        {
            logger.LogInformation("Starting video processing for asset {AssetId}", assetId);

            var asset = await assetRepository.GetByIdAsync(assetId, ct);
            if (asset == null)
            {
                logger.LogWarning("Asset {AssetId} not found, skipping processing", assetId);
                return;
            }

            // Generate a presigned URL so FFmpeg can stream directly from MinIO
            // instead of downloading the entire video file to disk.
            var presignedUrl = await minioAdapter.GetInternalPresignedDownloadUrlAsync(
                _bucketName, originalObjectKey, expirySeconds: 600, ct);

            // Extract poster frame (medium size for preview)
            await ExtractFrameAsync(presignedUrl, posterPath, _imageSettings.PosterFrameSeconds, _imageSettings.PosterWidth, ct);

            // Extract thumbnail (small size for grid display)
            await ExtractFrameAsync(presignedUrl, thumbPath, _imageSettings.PosterFrameSeconds, _imageSettings.ThumbnailWidth, ct);

            // Upload poster and thumbnail in parallel
            var posterKey = $"{Constants.StoragePrefixes.Posters}/{assetId}-poster.jpg";
            var thumbKey = $"{Constants.StoragePrefixes.Thumbnails}/{assetId}-thumb.jpg";

            await Task.WhenAll(
                UploadFileAsync(posterPath, posterKey, ct),
                UploadFileAsync(thumbPath, thumbKey, ct));

            // Update asset with poster and thumbnail
            asset.MarkReady(thumbKey, posterKey: posterKey);
            await assetRepository.UpdateAsync(asset, ct);

            sw.Stop();
            logger.LogInformation("Successfully processed video asset {AssetId} in {ElapsedMs}ms", assetId, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Video processing failed for asset {AssetId} after {ElapsedMs}ms: {Error}", assetId, sw.ElapsedMilliseconds, ex.Message);

            await auditService.LogAsync(
                "asset.processing_failed",
                Constants.ScopeTypes.Asset,
                assetId,
                actorUserId: null,
                new Dictionary<string, object>
                {
                    ["assetType"] = "video",
                    ["error"] = ex.Message,
                    ["errorType"] = ex.GetType().Name
                },
                ct);

            var asset = await assetRepository.GetByIdAsync(assetId, ct);
            if (asset != null)
            {
                asset.MarkFailed("Video processing failed. Please try uploading again or contact an administrator.");
                await assetRepository.UpdateAsync(asset, ct);
            }

            throw; // Re-throw so Hangfire can retry
        }
        finally
        {
            ProcessRunner.CleanupTempFile(posterPath, logger);
            ProcessRunner.CleanupTempFile(thumbPath, logger);
        }
    }

    private async Task UploadFileAsync(string localPath, string objectKey, CancellationToken ct)
    {
        using var fs = File.OpenRead(localPath);
        await minioAdapter.UploadAsync(_bucketName, objectKey, fs, Constants.ContentTypes.Jpeg, ct);
    }

    private async Task ExtractFrameAsync(string inputUrl, string outputPath, int atSecond, int width, CancellationToken ct)
    {
        var ffmpegPath = OperatingSystem.IsWindows() ? "ffmpeg" : "/usr/bin/ffmpeg";
        var command = ProcessRunner.CreateStartInfo(ffmpegPath);
        command.ArgumentList.Add("-ss");
        command.ArgumentList.Add(atSecond.ToString());
        command.ArgumentList.Add("-i");
        command.ArgumentList.Add(inputUrl);
        command.ArgumentList.Add("-vframes");
        command.ArgumentList.Add("1");
        command.ArgumentList.Add("-vf");
        command.ArgumentList.Add($"scale={width}:-1");
        command.ArgumentList.Add("-q:v");
        command.ArgumentList.Add(_imageSettings.PosterQuality.ToString());
        command.ArgumentList.Add(outputPath);
        command.ArgumentList.Add("-y");

        await ProcessRunner.RunAsync(ffmpegPath, command, ProcessTimeout, logger, ct);
    }
}
