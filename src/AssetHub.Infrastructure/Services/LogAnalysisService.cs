using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Parses plain-text log files and produces error frequency and trend metrics.
/// Supports Standard, Serilog, JSON-per-line, and Apache/Nginx log formats.
/// </summary>
public sealed partial class LogAnalysisService(ILogger<LogAnalysisService> logger) : ILogAnalysisService
{
    private const int MaxLines = 500_000;
    private const int MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    private const int TopMessageCount = 20;
    private const int MaxMessageLength = 200;

    // ── Regex patterns ────────────────────────────────────────────────────────

    // Standard: "2024-01-15 10:30:00.123 ERROR Some message"
    [GeneratedRegex(
        @"^(?<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\s+(?<level>TRACE|DEBUG|INFO(?:RMATION)?|WARN(?:ING)?|ERROR|FATAL|CRITICAL)\s*(?:\[.*?\])?\s*(?<msg>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StandardLogRegex();

    // Serilog compact: "[10:30:00 ERR] Message"  or "[2024-01-15 10:30:00 ERR] Message"
    [GeneratedRegex(
        @"^\[(?<ts>\d{4}-\d{2}-\d{2}\s+)?\d{2}:\d{2}:\d{2}(?:\.\d+)?\s+(?<level>VRB|DBG|INF|WRN|ERR|FTL)\]\s+(?<msg>.+)$",
        RegexOptions.Compiled)]
    private static partial Regex SerilogCompactRegex();

    // Apache/Nginx error: "[Mon Jan 15 10:30:00 2024] [error] message"
    [GeneratedRegex(
        @"\[(?<ts>[A-Za-z]{3} [A-Za-z]{3}\s+\d{1,2} \d{2}:\d{2}:\d{2} \d{4})\] \[(?<level>debug|info|notice|warn(?:ing)?|error|crit(?:ical)?|alert|emerg)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ApacheLogRegex();

    // Numbers, UUIDs, hex strings – replaced when normalizing messages
    [GeneratedRegex(
        @"\b(?:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\b0x[0-9a-fA-F]+\b|\d+(?:\.\d+)*)\b",
        RegexOptions.Compiled)]
    private static partial Regex NoisyTokenRegex();

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<ServiceResult<LogAnalysisResult>> AnalyzeAsync(
        Stream stream, string fileName, CancellationToken ct = default)
    {
        if (stream.Length > MaxFileSizeBytes)
            return ServiceError.BadRequest($"Log file exceeds the maximum allowed size of {MaxFileSizeBytes / 1024 / 1024} MB.");

        List<ParsedLogEntry> entries;
        string detectedFormat;

        try
        {
            (entries, detectedFormat) = await ParseEntriesAsync(stream, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to parse log file {FileName}", fileName);
            return ServiceError.BadRequest("Could not parse the log file. Ensure the file is plain text.");
        }

        var result = BuildResult(entries, fileName, detectedFormat);
        logger.LogInformation("Analyzed log file {FileName}: {Lines} lines, format={Format}",
            fileName, result.TotalLines, result.DetectedFormat);
        return result;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    private static async Task<(List<ParsedLogEntry> Entries, string Format)> ParseEntriesAsync(
        Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var entries = new List<ParsedLogEntry>(1024);
        int lineCount = 0;
        string? firstNonEmpty = null;

        while (lineCount < MaxLines)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            lineCount++;

            if (string.IsNullOrWhiteSpace(line)) continue;
            firstNonEmpty ??= line.TrimStart();
        }

        // Detect format from first non-empty line
        var format = DetectFormat(firstNonEmpty ?? string.Empty);

        // Rewind and do a real parse pass
        stream.Position = 0;
        using var reader2 = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        int parsed = 0;
        while (parsed < MaxLines)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader2.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var entry = ParseLine(line, format);
            entries.Add(entry);
            parsed++;
        }

        return (entries, format);
    }

    private static string DetectFormat(string firstLine)
    {
        if (string.IsNullOrWhiteSpace(firstLine)) return "Unknown";
        if (firstLine.StartsWith('{')) return "JSON";
        if (firstLine.StartsWith('[') && SerilogCompactRegex().IsMatch(firstLine)) return "Serilog";
        if (ApacheLogRegex().IsMatch(firstLine)) return "Apache";
        if (StandardLogRegex().IsMatch(firstLine)) return "Standard";
        return "Unknown";
    }

    private static ParsedLogEntry ParseLine(string line, string format)
    {
        return format switch
        {
            "JSON" => ParseJsonLine(line),
            "Serilog" => ParseWithRegex(line, SerilogCompactRegex(), MapSerilogLevel),
            "Apache" => ParseWithRegex(line, ApacheLogRegex(), s => NormalizeLevel(s)),
            "Standard" => ParseWithRegex(line, StandardLogRegex(), s => NormalizeLevel(s)),
            _ => TryAllFormats(line),
        };
    }

    private static ParsedLogEntry TryAllFormats(string line)
    {
        var m = StandardLogRegex().Match(line);
        if (m.Success) return ExtractFromMatch(m, s => NormalizeLevel(s));
        m = SerilogCompactRegex().Match(line);
        if (m.Success) return ExtractFromMatch(m, MapSerilogLevel);
        m = ApacheLogRegex().Match(line);
        if (m.Success) return ExtractFromMatch(m, s => NormalizeLevel(s));
        return new ParsedLogEntry { Message = line.Length > MaxMessageLength ? line[..MaxMessageLength] : line };
    }

    private static ParsedLogEntry ParseWithRegex(string line, Regex regex, Func<string, string> levelMapper)
    {
        var m = regex.Match(line);
        return m.Success ? ExtractFromMatch(m, levelMapper) : new ParsedLogEntry { Message = line.Length > MaxMessageLength ? line[..MaxMessageLength] : line };
    }

    private static ParsedLogEntry ExtractFromMatch(Match m, Func<string, string> levelMapper)
    {
        var tsRaw = m.Groups["ts"].Value;
        _ = DateTime.TryParse(tsRaw, out var ts);
        return new ParsedLogEntry
        {
            Timestamp = ts == default ? null : ts.ToUniversalTime(),
            Level = levelMapper(m.Groups["level"].Value),
            Message = m.Groups["msg"].Success ? m.Groups["msg"].Value.Trim() : string.Empty
        };
    }

    private static ParsedLogEntry ParseJsonLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var level = TryGetJsonString(root, "level", "@l", "severity", "log.level") ?? string.Empty;
            var msg = TryGetJsonString(root, "message", "@m", "msg", "message_template", "@mt") ?? line;
            var tsRaw = TryGetJsonString(root, "timestamp", "@t", "time", "@timestamp");
            DateTime ts = default;
            if (tsRaw is not null) DateTime.TryParse(tsRaw, out ts);
            return new ParsedLogEntry
            {
                Timestamp = ts == default ? null : ts.ToUniversalTime(),
                Level = NormalizeLevel(level),
                Message = msg.Length > MaxMessageLength ? msg[..MaxMessageLength] : msg
            };
        }
        catch
        {
            return new ParsedLogEntry { Message = line.Length > MaxMessageLength ? line[..MaxMessageLength] : line };
        }
    }

    private static string? TryGetJsonString(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (root.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                return val.GetString();
        }
        return null;
    }

    private static string NormalizeLevel(string raw) => raw.ToUpperInvariant() switch
    {
        "ERROR" or "ERR" or "FATAL" or "CRITICAL" or "CRIT" or "EMERG" or "ALERT" => "ERROR",
        "WARNING" or "WARN" or "WRN" => "WARN",
        "INFORMATION" or "INFO" or "INF" or "NOTICE" => "INFO",
        "DEBUG" or "DBG" or "TRACE" or "VRB" => "DEBUG",
        _ => string.IsNullOrWhiteSpace(raw) ? "UNKNOWN" : raw.ToUpperInvariant()
    };

    private static string MapSerilogLevel(string raw) => raw.ToUpperInvariant() switch
    {
        "FTL" or "ERR" => "ERROR",
        "WRN" => "WARN",
        "INF" => "INFO",
        "DBG" or "VRB" => "DEBUG",
        _ => raw.ToUpperInvariant()
    };

    // ── Result building ───────────────────────────────────────────────────────

    private static LogAnalysisResult BuildResult(
        List<ParsedLogEntry> entries, string fileName, string format)
    {
        var result = new LogAnalysisResult
        {
            FileName = fileName,
            TotalLines = entries.Count,
            DetectedFormat = format
        };

        // ── Counts by level ──────────────────────────────────────────────
        foreach (var e in entries)
        {
            var level = string.IsNullOrEmpty(e.Level) ? "UNKNOWN" : e.Level;
            result.CountByLevel.TryGetValue(level, out var existing);
            result.CountByLevel[level] = existing + 1;
            if (string.IsNullOrEmpty(e.Level)) result.UnparsedLines++;
        }

        // ── Timestamps ───────────────────────────────────────────────────
        var timestamps = entries
            .Where(e => e.Timestamp.HasValue)
            .Select(e => e.Timestamp!.Value)
            .OrderBy(t => t)
            .ToList();

        if (timestamps.Count > 0)
        {
            result.EarliestTimestamp = timestamps[0];
            result.LatestTimestamp = timestamps[^1];
        }

        // ── Top messages ─────────────────────────────────────────────────
        var significantLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ERROR", "WARN", "FATAL", "CRITICAL" };

        var freqMap = new Dictionary<string, (string Level, int Count)>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (!significantLevels.Contains(e.Level ?? string.Empty)) continue;
            if (string.IsNullOrWhiteSpace(e.Message)) continue;
            var key = NoisyTokenRegex().Replace(e.Message, "#");
            key = key.Length > MaxMessageLength ? key[..MaxMessageLength] : key;
            if (freqMap.TryGetValue(key, out var existing))
                freqMap[key] = (existing.Level, existing.Count + 1);
            else
                freqMap[key] = (e.Level!, 1);
        }

        result.TopMessages = freqMap
            .OrderByDescending(kv => kv.Value.Count)
            .Take(TopMessageCount)
            .Select(kv => new LogEntryFrequency
            {
                Message = kv.Key,
                Level = kv.Value.Level,
                Count = kv.Value.Count
            })
            .ToList();

        // ── Trend data ───────────────────────────────────────────────────
        if (timestamps.Count > 0)
        {
            var span = result.LatestTimestamp!.Value - result.EarliestTimestamp!.Value;
            var (granularity, bucketFn, labelFn) = ChooseGranularity(span);
            result.TrendGranularity = granularity;

            var buckets = new SortedDictionary<DateTime, (int Errors, int Warnings, int Info)>();
            foreach (var e in entries.Where(e => e.Timestamp.HasValue))
            {
                var bucket = bucketFn(e.Timestamp!.Value);
                buckets.TryGetValue(bucket, out var cur);
                var level = e.Level ?? string.Empty;
                buckets[bucket] = level switch
                {
                    "ERROR" => (cur.Errors + 1, cur.Warnings, cur.Info),
                    "WARN" => (cur.Errors, cur.Warnings + 1, cur.Info),
                    _ => (cur.Errors, cur.Warnings, cur.Info + 1)
                };
            }

            result.TrendData = buckets
                .Select(kv => new LogTrendPoint
                {
                    Timestamp = kv.Key,
                    Label = labelFn(kv.Key),
                    Errors = kv.Value.Errors,
                    Warnings = kv.Value.Warnings,
                    Info = kv.Value.Info
                })
                .ToList();
        }

        return result;
    }

    private static (string Granularity, Func<DateTime, DateTime> Bucket, Func<DateTime, string> Label)
        ChooseGranularity(TimeSpan span)
    {
        if (span.TotalMinutes <= 120)
            return ("minute",
                dt => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, DateTimeKind.Utc),
                dt => dt.ToString("HH:mm"));

        if (span.TotalHours <= 72)
            return ("hour",
                dt => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, DateTimeKind.Utc),
                dt => dt.ToString("MM-dd HH:mm"));

        return ("day",
            dt => new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc),
            dt => dt.ToString("yyyy-MM-dd"));
    }

    // ── Inner type ────────────────────────────────────────────────────────────

    private sealed class ParsedLogEntry
    {
        public DateTime? Timestamp { get; init; }
        public string? Level { get; init; }
        public string Message { get; set; } = string.Empty;
    }
}
