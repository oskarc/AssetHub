using System.Diagnostics;
using System.Text.Json;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Helpers;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Result of audio processing — returned to the consumer for DB update and event publication.
/// </summary>
public sealed record AudioProcessingResult
{
    public bool Succeeded { get; init; }
    public int? DurationSeconds { get; init; }
    public int? AudioBitrateKbps { get; init; }
    public int? AudioSampleRateHz { get; init; }
    public int? AudioChannels { get; init; }
    public string? WaveformPeaksPath { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }

    public static AudioProcessingResult Success(int? duration, int? bitrate, int? sampleRate, int? channels, string peaksPath)
        => new()
        {
            Succeeded = true,
            DurationSeconds = duration,
            AudioBitrateKbps = bitrate,
            AudioSampleRateHz = sampleRate,
            AudioChannels = channels,
            WaveformPeaksPath = peaksPath
        };

    public static AudioProcessingResult Failure(string message, string errorType)
        => new() { Succeeded = false, ErrorMessage = message, ErrorType = errorType };
}

/// <summary>
/// Processes audio assets: probes metadata via ffprobe and generates a waveform-peaks JSON
/// via ffmpeg. Returns a result object — the caller (Wolverine handler) handles DB updates.
/// </summary>
/// <remarks>
/// Strict failure mode (T5-AUDIO-01 Q4): either ffprobe or ffmpeg-peaks failure causes the
/// asset to be marked Failed by the calling handler. v1 generates peaks even though no UI
/// renders them yet — see Q2(c) in the grill-me record. The peaks file shape is
/// min/max pairs scaled to roughly <c>min(duration_s × 10, 4000)</c> buckets with a 500
/// floor; canvas-overlay UI work later lights this data up without regenerating.
/// </remarks>
public sealed class AudioProcessingService(
    IMinIOAdapter minioAdapter,
    IOptions<MinIOSettings> minioSettings,
    ILogger<AudioProcessingService> logger)
{
    private readonly string _bucketName = minioSettings.Value.BucketName;

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    private const int MinPeakBuckets = 500;
    private const int MaxPeakBuckets = 4000;
    private const int BucketsPerSecond = 10;

    // PCM_S16LE at 8 kHz mono — small, deterministic, plenty of resolution
    // for a waveform visualisation. ffmpeg downmixes anything to mono so we
    // only have to handle a single channel of int16 samples.
    private const int PeaksSampleRateHz = 8000;

    public async Task<AudioProcessingResult> ProcessAudioAsync(Guid assetId, string originalObjectKey, CancellationToken ct = default)
    {
        var pcmPath = ScratchPaths.Combine($"{Guid.NewGuid()}.pcm");
        var sw = Stopwatch.StartNew();

        try
        {
            logger.LogInformation("Starting audio processing for asset {AssetId}", assetId);

            // Use a short-lived presigned URL so ffprobe / ffmpeg can stream
            // directly from MinIO without us first staging the whole file
            // to disk.
            var presignedUrl = await minioAdapter.GetInternalPresignedDownloadUrlAsync(
                _bucketName, originalObjectKey, expirySeconds: 600, ct);

            var probe = await ProbeAsync(presignedUrl, ct);

            await ExtractPcmAsync(presignedUrl, pcmPath, ct);

            var bucketCount = ChoosePeakBucketCount(probe.DurationSeconds);
            var pcmBytes = await File.ReadAllBytesAsync(pcmPath, ct);
            var peaks = ComputePeaks(pcmBytes, bucketCount);

            var peaksKey = $"{Constants.StoragePrefixes.Peaks}/{assetId}.json";
            await UploadPeaksAsync(peaks, peaksKey, ct);

            sw.Stop();
            logger.LogInformation(
                "Successfully processed audio asset {AssetId} in {ElapsedMs}ms (duration={Duration}s, buckets={BucketCount})",
                assetId, sw.ElapsedMilliseconds, probe.DurationSeconds, bucketCount);

            return AudioProcessingResult.Success(
                probe.DurationSeconds,
                probe.BitrateKbps,
                probe.SampleRateHz,
                probe.Channels,
                peaksKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audio processing failed for asset {AssetId} after {ElapsedMs}ms: {Error}", assetId, sw.ElapsedMilliseconds, ex.Message);
            return AudioProcessingResult.Failure(ex.Message, ex.GetType().Name);
        }
        finally
        {
            ProcessRunner.CleanupTempFile(pcmPath, logger);
        }
    }

    private async Task<AudioProbe> ProbeAsync(string inputUrl, CancellationToken ct)
    {
        var ffprobePath = OperatingSystem.IsWindows() ? "ffprobe" : "/usr/bin/ffprobe";
        var command = ProcessRunner.CreateStartInfo(ffprobePath);
        command.ArgumentList.Add("-v");
        command.ArgumentList.Add("error");
        command.ArgumentList.Add("-show_format");
        command.ArgumentList.Add("-show_streams");
        command.ArgumentList.Add("-select_streams");
        command.ArgumentList.Add("a:0");
        command.ArgumentList.Add("-print_format");
        command.ArgumentList.Add("json");
        command.ArgumentList.Add(inputUrl);

        var stdout = await ProcessRunner.RunAndCaptureStdoutAsync(ffprobePath, command, ProcessTimeout, logger, ct);
        return AudioProbe.Parse(stdout);
    }

    private async Task ExtractPcmAsync(string inputUrl, string outputPath, CancellationToken ct)
    {
        var ffmpegPath = OperatingSystem.IsWindows() ? "ffmpeg" : "/usr/bin/ffmpeg";
        var command = ProcessRunner.CreateStartInfo(ffmpegPath);
        command.ArgumentList.Add("-i");
        command.ArgumentList.Add(inputUrl);
        command.ArgumentList.Add("-ac");
        command.ArgumentList.Add("1");                       // downmix to mono
        command.ArgumentList.Add("-filter:a");
        command.ArgumentList.Add($"aresample={PeaksSampleRateHz}");
        command.ArgumentList.Add("-map");
        command.ArgumentList.Add("0:a");
        command.ArgumentList.Add("-c:a");
        command.ArgumentList.Add("pcm_s16le");
        command.ArgumentList.Add("-f");
        command.ArgumentList.Add("s16le");                   // raw PCM — no container
        command.ArgumentList.Add(outputPath);
        command.ArgumentList.Add("-y");

        await ProcessRunner.RunAsync(ffmpegPath, command, ProcessTimeout, logger, ct);
    }

    /// <summary>
    /// Choose an adaptive bucket count that scales with audio duration.
    /// Short clips get the floor (500); long files cap at 4000 so the JSON
    /// stays under ~80 KB. Future canvas-overlay work picks a render width
    /// less than the cap so all buckets fit on screen.
    /// </summary>
    public static int ChoosePeakBucketCount(int? durationSeconds)
    {
        if (durationSeconds is null or <= 0)
            return MinPeakBuckets;
        var raw = durationSeconds.Value * BucketsPerSecond;
        return Math.Clamp(raw, MinPeakBuckets, MaxPeakBuckets);
    }

    /// <summary>
    /// Read the PCM_S16LE file and downsample to <paramref name="bucketCount"/>
    /// min/max pairs. Each pair is [min, max] in the range [-1.0, 1.0]. The
    /// pair shape (rather than a single peak) lets a future canvas overlay
    /// render the symmetric waveform expected by libraries like peaks.js.
    /// </summary>
    public static double[][] ComputePeaks(byte[] bytes, int bucketCount)
    {
        var sampleCount = bytes.Length / 2; // 16-bit = 2 bytes per sample
        var peaks = new double[bucketCount][];

        if (sampleCount == 0)
        {
            for (var i = 0; i < bucketCount; i++) peaks[i] = [0d, 0d];
            return peaks;
        }

        var samplesPerBucket = (double)sampleCount / bucketCount;

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var startSample = (int)(bucket * samplesPerBucket);
            var endSample = (int)((bucket + 1) * samplesPerBucket);
            if (endSample > sampleCount) endSample = sampleCount;

            short min = 0;
            short max = 0;
            for (var i = startSample; i < endSample; i++)
            {
                var sample = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
                if (sample < min) min = sample;
                if (sample > max) max = sample;
            }

            // Normalise to [-1, 1]. Int16.MinValue is -32768 so divide by that
            // for negative; Int16.MaxValue is 32767 for positive — using the
            // negative magnitude for both keeps the range symmetric, which the
            // peaks.js consumer expects.
            peaks[bucket] = [min / 32768d, max / 32768d];
        }

        return peaks;
    }

    private async Task UploadPeaksAsync(double[][] peaks, string peaksKey, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(peaks);
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await minioAdapter.UploadAsync(_bucketName, peaksKey, ms, Constants.ContentTypes.Json, ct);
    }

    public sealed record AudioProbe(int? DurationSeconds, int? BitrateKbps, int? SampleRateHz, int? Channels)
    {
        public static AudioProbe Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int? duration = null;
            int? bitrate = null;
            int? sampleRate = null;
            int? channels = null;

            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ds))
                {
                    duration = (int)Math.Round(ds);
                }
                if (format.TryGetProperty("bit_rate", out var br) && long.TryParse(br.GetString(),
                    System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var brBps))
                {
                    bitrate = (int)Math.Round(brBps / 1000.0);
                }
            }

            if (root.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0)
            {
                var stream = streams[0];
                if (stream.TryGetProperty("sample_rate", out var sr) && int.TryParse(sr.GetString(),
                    System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var srValue))
                {
                    sampleRate = srValue;
                }
                if (stream.TryGetProperty("channels", out var ch) && ch.TryGetInt32(out var chValue))
                {
                    channels = chValue;
                }
                // Per-stream bit_rate beats container bit_rate when both exist
                // (e.g. an .mka with bitrate-less format header).
                if (stream.TryGetProperty("bit_rate", out var sbr) && long.TryParse(sbr.GetString(),
                    System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sbrBps))
                {
                    bitrate = (int)Math.Round(sbrBps / 1000.0);
                }
            }

            return new AudioProbe(duration, bitrate, sampleRate, channels);
        }
    }
}
