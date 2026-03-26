using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Processes video assets: extracts a poster frame thumbnail.
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

    public async Task ProcessVideoAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default)
    {
        var tempOriginal = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
        }
        finally
        {
            CleanupTempFile(tempOriginal);
            CleanupTempFile(posterPath);
        }
    }

    private async Task ExtractPosterAsync(string inputPath, string outputPath, int atSecond, CancellationToken ct = default)
    {
        var ffmpegPath = OperatingSystem.IsWindows() ? "ffmpeg" : "/usr/bin/ffmpeg";
        var command = new ProcessStartInfo(ffmpegPath)
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

        await RunProcessAsync(ffmpegPath, command, ct);
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
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { }
            throw new TimeoutException($"{toolName} process exceeded the {ProcessTimeout.TotalMinutes:F0}-minute timeout and was killed");
        }
        catch
        {
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { }
            throw;
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
