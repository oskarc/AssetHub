using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Parses and analyzes uploaded log files, returning error frequencies and trend data.
/// </summary>
public interface ILogAnalysisService
{
    /// <summary>
    /// Analyzes the content of a log file and returns structured metrics.
    /// </summary>
    /// <param name="stream">The log file byte stream (plain text).</param>
    /// <param name="fileName">The original file name (used in the result).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ServiceResult<LogAnalysisResult>> AnalyzeAsync(
        Stream stream, string fileName, CancellationToken ct = default);
}
