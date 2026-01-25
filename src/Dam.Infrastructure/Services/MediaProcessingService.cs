using Dam.Application.Repositories;
using Dam.Application.Services;
using Hangfire;
using Hangfire.Storage;
using System.Diagnostics;

namespace Dam.Infrastructure.Services;

public class MediaProcessingService(
    IAssetRepository assetRepository,
    IMinIOAdapter minioAdapter,
    IBackgroundJobClient backgroundJobClient) : IMediaProcessingService
{
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
            using var originalStream = await minioAdapter.DownloadAsync("assethub-dev", originalObjectKey);
            var tempOriginal = Path.GetTempFileName();
            using (var fs = File.Create(tempOriginal))
            {
                await originalStream.CopyToAsync(fs);
            }

            // Create thumbnail
            var thumbPath = Path.GetTempFileName();
            await ResizeImageAsync(tempOriginal, thumbPath, ThumbWidth, ThumbHeight);
            var thumbKey = $"thumbs/{assetId}-thumb.jpg";
            using (var fs = File.OpenRead(thumbPath))
            {
                await minioAdapter.UploadAsync("assethub-dev", thumbKey, fs, "image/jpeg");
            }

            // Create medium version
            var mediumPath = Path.GetTempFileName();
            await ResizeImageAsync(tempOriginal, mediumPath, MediumWidth, MediumHeight);
            var mediumKey = $"medium/{assetId}-medium.jpg";
            using (var fs = File.OpenRead(mediumPath))
            {
                await minioAdapter.UploadAsync("assethub-dev", mediumKey, fs, "image/jpeg");
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
            using var originalStream = await minioAdapter.DownloadAsync("assethub-dev", originalObjectKey);
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
                await minioAdapter.UploadAsync("assethub-dev", posterKey, fs, "image/jpeg");
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
}
