using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Runs external CLI tools (ImageMagick, FFmpeg) with timeout and error handling.
/// </summary>
internal static class ProcessRunner
{
    internal static async Task RunAsync(string toolName, ProcessStartInfo startInfo, TimeSpan timeout, ILogger logger, CancellationToken ct)
    {
        _ = await RunInternalAsync(toolName, startInfo, timeout, logger, ct);
    }

    /// <summary>
    /// Run an external tool and return its stdout as a string. ffprobe writes
    /// JSON to stdout by default, so this is the natural shape for tools
    /// whose output is text we need to parse rather than a file artifact.
    /// </summary>
    internal static async Task<string> RunAndCaptureStdoutAsync(string toolName, ProcessStartInfo startInfo, TimeSpan timeout, ILogger logger, CancellationToken ct)
    {
        return await RunInternalAsync(toolName, startInfo, timeout, logger, ct);
    }

    private static async Task<string> RunInternalAsync(string toolName, ProcessStartInfo startInfo, TimeSpan timeout, ILogger logger, CancellationToken ct)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException($"{toolName} process failed to start");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* Best-effort drain of stdio after kill — exceptions are non-actionable */ }
            throw new TimeoutException($"{toolName} process exceeded the {timeout.TotalMinutes:F0}-minute timeout and was killed");
        }
        catch
        {
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* Best-effort drain of stdio after failure — exceptions are non-actionable */ }
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            logger.LogWarning("{Tool} stderr: {StdErr}", toolName, stderr);
            throw new InvalidOperationException($"{toolName} error (exit code {process.ExitCode}): {stderr}");
        }

        return stdout;
    }

    internal static ProcessStartInfo CreateStartInfo(string executable)
    {
        return new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    internal static void CleanupTempFile(string path, ILogger logger)
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
