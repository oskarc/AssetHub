using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// Result of analyzing an uploaded log file.
/// </summary>
public class LogAnalysisResult
{
    /// <summary>Name of the analyzed file.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Total number of log lines parsed.</summary>
    public int TotalLines { get; set; }

    /// <summary>Total number of lines that could not be parsed.</summary>
    public int UnparsedLines { get; set; }

    /// <summary>Detected log format (e.g. "Standard", "Serilog", "JSON", "Apache", "Unknown").</summary>
    public string DetectedFormat { get; set; } = string.Empty;

    /// <summary>Earliest timestamp found in the log, if any.</summary>
    public DateTime? EarliestTimestamp { get; set; }

    /// <summary>Latest timestamp found in the log, if any.</summary>
    public DateTime? LatestTimestamp { get; set; }

    /// <summary>Count of entries by log level (e.g. "ERROR" → 42).</summary>
    public Dictionary<string, int> CountByLevel { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The most frequently occurring error/warning messages.</summary>
    public List<LogEntryFrequency> TopMessages { get; set; } = [];

    /// <summary>Number of log entries per time bucket (for trend chart).</summary>
    public List<LogTrendPoint> TrendData { get; set; } = [];

    /// <summary>Bucket granularity used for trend data ("hour", "day", "minute").</summary>
    public string TrendGranularity { get; set; } = string.Empty;
}

/// <summary>
/// A frequently occurring log message and how many times it appeared.
/// </summary>
public class LogEntryFrequency
{
    /// <summary>Normalized message text (numbers and UUIDs replaced with placeholders).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Log level of this message.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Number of times this message occurred.</summary>
    public int Count { get; set; }
}

/// <summary>
/// A single point in the log trend chart.
/// </summary>
public class LogTrendPoint
{
    /// <summary>Start of the time bucket (UTC).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Human-readable label for the bucket (e.g. "2024-01-15 10:00").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Error count in this bucket.</summary>
    public int Errors { get; set; }

    /// <summary>Warning count in this bucket.</summary>
    public int Warnings { get; set; }

    /// <summary>Info/other count in this bucket.</summary>
    public int Info { get; set; }
}
