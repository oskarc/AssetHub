using System.Diagnostics;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Helpers;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Result of video processing — returned to the consumer for DB update and event publication.
/// </summary>
public sealed record VideoProcessingResult
{
    public bool Succeeded { get; init; }
    public string? ThumbObjectKey { get; init; }
    public string? PosterObjectKey { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }

    public static VideoProcessingResult Success(string thumbKey, string posterKey)
        => new() { Succeeded = true, ThumbObjectKey = thumbKey, PosterObjectKey = posterKey };

    public static VideoProcessingResult Failure(string message, string errorType)
        => new() { Succeeded = false, ErrorMessage = message, ErrorType = errorType };
}

/// <summary>
/// Processes video assets: extracts a poster frame and thumbnail.
/// Uses presigned URLs so FFmpeg can stream directly from MinIO without downloading the entire file.
/// Returns a result object — the caller (Wolverine handler) is responsible for DB updates.
/// </summary>
public sealed class VideoProcessingService(
    IMinIOAdapter minioAdapter,
    IOptions<MinIOSettings> minioSettings,
    IOptions<ImageProcessingSettings> imageProcessingSettings,
    ILogger<VideoProcessingService> logger)
{
    private readonly string _bucketName = minioSettings.Value.BucketName;
    private readonly ImageProcessingSettings _imageSettings = imageProcessingSettings.Value;

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    public async Task<VideoProcessingResult> ProcessVideoAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default)
    {
        var posterPath = ScratchPaths.Combine($"{Guid.NewGuid()}.jpg");
        var thumbPath = ScratchPaths.Combine($"{Guid.NewGuid()}.jpg");
        var sw = Stopwatch.StartNew();

        try
        {
            logger.LogInformation("Starting video processing for asset {AssetId}", assetId);

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

            sw.Stop();
            logger.LogInformation("Successfully processed video asset {AssetId} in {ElapsedMs}ms", assetId, sw.ElapsedMilliseconds);

            return VideoProcessingResult.Success(thumbKey, posterKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Video processing failed for asset {AssetId} after {ElapsedMs}ms: {Error}", assetId, sw.ElapsedMilliseconds, ex.Message);
            return VideoProcessingResult.Failure(ex.Message, ex.GetType().Name);
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
