using System.Text;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit tests for LogAnalysisService — log file parsing and analysis.
/// These are pure unit tests that do not require a database.
/// </summary>
public class LogAnalysisServiceTests
{
    private readonly LogAnalysisService _svc = new(NullLogger<LogAnalysisService>.Instance);

    // ── Standard format ───────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_StandardFormat_DetectsFormatAndCounts()
    {
        const string log = """
            2024-01-15 10:00:00 ERROR Something went wrong
            2024-01-15 10:01:00 WARN  Resource not found
            2024-01-15 10:02:00 INFO  Application started
            2024-01-15 10:03:00 DEBUG Debug trace
            2024-01-15 10:04:00 ERROR Disk full
            """;

        var result = await AnalyzeStringAsync(log, "test.log");

        Assert.True(result.IsSuccess);
        var data = result.Value!;
        Assert.Equal("Standard", data.DetectedFormat);
        Assert.Equal(5, data.TotalLines);
        Assert.Equal(2, data.CountByLevel["ERROR"]);
        Assert.Equal(1, data.CountByLevel["WARN"]);
        Assert.Equal(1, data.CountByLevel["INFO"]);
        Assert.Equal(1, data.CountByLevel["DEBUG"]);
    }

    [Fact]
    public async Task AnalyzeAsync_StandardFormat_ExtractsTimestamps()
    {
        const string log = """
            2024-01-15 08:00:00 ERROR First
            2024-01-15 12:30:00 ERROR Last
            """;

        var result = await AnalyzeStringAsync(log, "test.log");

        Assert.True(result.IsSuccess);
        var data = result.Value!;
        Assert.NotNull(data.EarliestTimestamp);
        Assert.NotNull(data.LatestTimestamp);
        Assert.True(data.EarliestTimestamp < data.LatestTimestamp);
    }

    // ── Serilog compact format ─────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_SerilogFormat_DetectsAndCounts()
    {
        const string log = """
            [10:00:00 ERR] Database connection failed
            [10:01:00 WRN] Retry attempt 1
            [10:02:00 INF] Request completed
            [10:03:00 DBG] Cache miss
            """;

        var result = await AnalyzeStringAsync(log, "serilog.log");

        Assert.True(result.IsSuccess);
        var data = result.Value!;
        Assert.Equal("Serilog", data.DetectedFormat);
        Assert.Equal(1, data.CountByLevel["ERROR"]);
        Assert.Equal(1, data.CountByLevel["WARN"]);
    }

    // ── JSON format ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_JsonFormat_DetectsAndCounts()
    {
        const string log = """
            {"timestamp":"2024-01-15T10:00:00Z","level":"Error","message":"NullReferenceException"}
            {"timestamp":"2024-01-15T10:01:00Z","level":"Warning","message":"Slow query"}
            {"timestamp":"2024-01-15T10:02:00Z","level":"Information","message":"OK"}
            """;

        var result = await AnalyzeStringAsync(log, "app.jsonl");

        Assert.True(result.IsSuccess);
        var data = result.Value!;
        Assert.Equal("JSON", data.DetectedFormat);
        Assert.Equal(1, data.CountByLevel["ERROR"]);
        Assert.Equal(1, data.CountByLevel["WARN"]);
        Assert.Equal(1, data.CountByLevel["INFO"]);
    }

    // ── Top messages ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_RepeatedErrors_ProducesTopMessages()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 5; i++)
            sb.AppendLine("2024-01-15 10:00:00 ERROR NullReferenceException in Controller");
        for (int i = 0; i < 3; i++)
            sb.AppendLine("2024-01-15 10:00:00 WARN  Slow response: 2345ms");

        var result = await AnalyzeStringAsync(sb.ToString(), "test.log");

        Assert.True(result.IsSuccess);
        var data = result.Value!;
        Assert.NotEmpty(data.TopMessages);
        Assert.Equal(5, data.TopMessages[0].Count);
        Assert.Equal("ERROR", data.TopMessages[0].Level);
    }

    [Fact]
    public async Task AnalyzeAsync_NoErrors_TopMessagesIsEmpty()
    {
        const string log = """
            2024-01-15 10:00:00 INFO Application started
            2024-01-15 10:01:00 INFO Request completed
            """;

        var result = await AnalyzeStringAsync(log, "test.log");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.TopMessages);
    }

    // ── Trend data ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_MultipleHours_ProducesTrendData()
    {
        var sb = new StringBuilder();
        for (int h = 0; h < 3; h++)
            for (int m = 0; m < 5; m++)
                sb.AppendLine($"2024-01-15 {h:D2}:{m * 10:D2}:00 ERROR failure {h}-{m}");

        var result = await AnalyzeStringAsync(sb.ToString(), "test.log");

        Assert.True(result.IsSuccess);
        var data = result.Value!;
        Assert.NotEmpty(data.TrendData);
        Assert.True(data.TrendData.All(p => p.Errors >= 0));
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_EmptyFile_ReturnsZeroLines()
    {
        var result = await AnalyzeStringAsync(string.Empty, "empty.log");

        Assert.True(result.IsSuccess);
        var data = result.Value!;
        Assert.Equal(0, data.TotalLines);
        Assert.Empty(data.CountByLevel);
    }

    [Fact]
    public async Task AnalyzeAsync_UnknownFormat_StillParses()
    {
        const string log = """
            This is a random line that doesn't match any format
            Another random line with ERROR keyword buried inside
            """;

        var result = await AnalyzeStringAsync(log, "weird.log");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalLines);
    }

    [Fact]
    public async Task AnalyzeAsync_FileTooLarge_ReturnsBadRequest()
    {
        // Create a stream that reports a length above 50MB
        var bigStream = new FakeOversizedStream(51 * 1024 * 1024);
        var result = await _svc.AnalyzeAsync(bigStream, "big.log");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<AssetHub.Application.ServiceResult<AssetHub.Application.Dtos.LogAnalysisResult>>
        AnalyzeStringAsync(string content, string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        await using var stream = new MemoryStream(bytes);
        return await _svc.AnalyzeAsync(stream, fileName);
    }

    /// <summary>A stream that reports a large Length without actually holding that data.</summary>
    private sealed class FakeOversizedStream(long reportedLength) : MemoryStream(Array.Empty<byte>())
    {
        public override long Length => reportedLength;
    }
}
