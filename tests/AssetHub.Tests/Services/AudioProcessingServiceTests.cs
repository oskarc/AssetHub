using AssetHub.Infrastructure.Services;

namespace AssetHub.Tests.Services;

/// <summary>
/// Pure-function coverage for the parts of <see cref="AudioProcessingService"/>
/// that don't shell out to ffprobe / ffmpeg. The end-to-end ProcessAudioAsync
/// flow needs both binaries on the box and is exercised manually / in
/// future integration tests.
/// </summary>
public class AudioProcessingServiceTests
{
    // ── ChoosePeakBucketCount ────────────────────────────────────────────

    [Theory]
    [InlineData(null, 500)]    // unknown duration → floor
    [InlineData(0, 500)]       // zero → floor
    [InlineData(-5, 500)]      // negative shouldn't happen but stays safe
    [InlineData(10, 500)]      // 10 × 10 = 100, still below floor → floor
    [InlineData(60, 600)]      // 60s × 10 = 600 → linear region
    [InlineData(180, 1800)]    // 3min → linear
    [InlineData(400, 4000)]    // 400 × 10 = 4000 = cap
    [InlineData(3600, 4000)]   // 1hr → capped
    public void ChoosePeakBucketCount_RespectsFloorAndCap(int? duration, int expected)
    {
        Assert.Equal(expected, AudioProcessingService.ChoosePeakBucketCount(duration));
    }

    // ── ComputePeaks ─────────────────────────────────────────────────────

    [Fact]
    public void ComputePeaks_EmptyInput_ReturnsZeroPairs()
    {
        var peaks = AudioProcessingService.ComputePeaks([], bucketCount: 4);

        Assert.Equal(4, peaks.Length);
        Assert.All(peaks, pair =>
        {
            Assert.Equal(2, pair.Length);
            Assert.Equal(0d, pair[0]);
            Assert.Equal(0d, pair[1]);
        });
    }

    [Fact]
    public void ComputePeaks_NormalisedToUnitRange()
    {
        // 4 samples: -32768 (min), 32767 (max), 0, 16384.
        // Stored little-endian as int16: the byte pattern is [low, high] per sample.
        // Using int values written into the byte buffer to keep the test obvious.
        var bytes = new byte[8];
        WriteSampleLE(bytes, 0, -32768);
        WriteSampleLE(bytes, 1, 32767);
        WriteSampleLE(bytes, 2, 0);
        WriteSampleLE(bytes, 3, 16384);

        var peaks = AudioProcessingService.ComputePeaks(bytes, bucketCount: 1);

        Assert.Single(peaks);
        Assert.Equal(2, peaks[0].Length);
        // Min = -32768 / 32768 = -1.0
        Assert.Equal(-1.0d, peaks[0][0], precision: 5);
        // Max = 32767 / 32768 ≈ 0.99997
        Assert.InRange(peaks[0][1], 0.999d, 1.0d);
    }

    [Fact]
    public void ComputePeaks_BucketsSplitInputEvenly()
    {
        // 10 samples — mix of values. Bucket count = 5 → 2 samples per bucket.
        // Bucket 0: samples 0,1 → -1000, 2000
        // Bucket 1: samples 2,3 → -2000, 1000
        // ...
        var bytes = new byte[20];
        WriteSampleLE(bytes, 0, -1000);
        WriteSampleLE(bytes, 1,  2000);
        WriteSampleLE(bytes, 2, -2000);
        WriteSampleLE(bytes, 3,  1000);
        WriteSampleLE(bytes, 4,     0);
        WriteSampleLE(bytes, 5,     0);
        WriteSampleLE(bytes, 6,     0);
        WriteSampleLE(bytes, 7,     0);
        WriteSampleLE(bytes, 8,     0);
        WriteSampleLE(bytes, 9,     0);

        var peaks = AudioProcessingService.ComputePeaks(bytes, bucketCount: 5);

        Assert.Equal(5, peaks.Length);
        // First bucket: min=-1000, max=2000 → -1000/32768, 2000/32768
        Assert.Equal(-1000d / 32768d, peaks[0][0], precision: 6);
        Assert.Equal(2000d / 32768d, peaks[0][1], precision: 6);
        Assert.Equal(-2000d / 32768d, peaks[1][0], precision: 6);
        Assert.Equal(1000d / 32768d, peaks[1][1], precision: 6);
    }

    private static void WriteSampleLE(byte[] buf, int sampleIndex, int value)
    {
        var s = (short)value;
        buf[sampleIndex * 2] = (byte)(s & 0xFF);
        buf[sampleIndex * 2 + 1] = (byte)((s >> 8) & 0xFF);
    }

    // ── AudioProbe.Parse ─────────────────────────────────────────────────

    [Fact]
    public void Parse_FormatAndStreams_ExtractsAllFields()
    {
        const string json = """
        {
            "format": {
                "duration": "183.456",
                "bit_rate": "192000"
            },
            "streams": [
                {
                    "sample_rate": "44100",
                    "channels": 2,
                    "bit_rate": "256000"
                }
            ]
        }
        """;

        var probe = AudioProcessingService.AudioProbe.Parse(json);

        // Per-stream bit_rate beats container bit_rate — 256kbps wins.
        Assert.Equal(183, probe.DurationSeconds);
        Assert.Equal(256, probe.BitrateKbps);
        Assert.Equal(44100, probe.SampleRateHz);
        Assert.Equal(2, probe.Channels);
    }

    [Fact]
    public void Parse_OnlyFormat_FallsBackToContainerBitrate()
    {
        const string json = """
        {
            "format": {
                "duration": "10.0",
                "bit_rate": "128000"
            },
            "streams": [
                {
                    "sample_rate": "22050",
                    "channels": 1
                }
            ]
        }
        """;

        var probe = AudioProcessingService.AudioProbe.Parse(json);
        Assert.Equal(10, probe.DurationSeconds);
        Assert.Equal(128, probe.BitrateKbps);
    }

    [Fact]
    public void Parse_MissingFields_LeavesNulls()
    {
        const string json = "{ \"format\": {}, \"streams\": [] }";
        var probe = AudioProcessingService.AudioProbe.Parse(json);

        Assert.Null(probe.DurationSeconds);
        Assert.Null(probe.BitrateKbps);
        Assert.Null(probe.SampleRateHz);
        Assert.Null(probe.Channels);
    }
}
