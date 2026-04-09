using System.Net.Sockets;
using System.Text;
using AssetHub.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// ClamAV antivirus scanner implementation using the clamd TCP protocol.
/// Supports INSTREAM command for streaming file content directly to clamd.
/// Wraps TCP operations with a Polly resilience pipeline for retry and circuit-breaker.
/// </summary>
public sealed class ClamAvScannerService : IMalwareScannerService
{
    private readonly ILogger<ClamAvScannerService> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly bool _enabled;
    private readonly int _timeoutMs;
    private readonly int _chunkSize;
    private readonly ResiliencePipeline _pipeline;

    public ClamAvScannerService(
        IConfiguration configuration,
        ILogger<ClamAvScannerService> logger,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _logger = logger;
        _pipeline = pipelineProvider.GetPipeline("clamav");

        var section = configuration.GetSection("ClamAV");
        _enabled = section.GetValue("Enabled", false);
        _host = section.GetValue("Host", "clamav") ?? "clamav";
        _port = section.GetValue("Port", 3310);
        _timeoutMs = section.GetValue("TimeoutMs", 30000);
        _chunkSize = section.GetValue("ChunkSize", 8192);

        if (_enabled)
            _logger.LogInformation("ClamAV scanner enabled at {Host}:{Port}", _host, _port);
        else
            _logger.LogInformation("ClamAV scanner is disabled");
    }

    public async Task<MalwareScanResult> ScanAsync(Stream stream, string fileName, CancellationToken ct)
    {
        if (!_enabled)
        {
            _logger.LogDebug("Malware scan skipped (disabled): {FileName}", fileName);
            return MalwareScanResult.Skipped();
        }

        try
        {
            return await _pipeline.ExecuteAsync(async innerCt =>
            {
                // Reset stream position if seekable (important for retries)
                if (stream.CanSeek)
                    stream.Position = 0;

                using var client = new TcpClient();
                await client.ConnectAsync(_host, _port, innerCt);
                client.ReceiveTimeout = _timeoutMs;
                client.SendTimeout = _timeoutMs;

                await using var networkStream = client.GetStream();

                // Send INSTREAM command
                await networkStream.WriteAsync("zINSTREAM\0"u8.ToArray(), innerCt);

                // Stream file in chunks (length-prefixed in network byte order)
                var buffer = new byte[_chunkSize];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), innerCt)) > 0)
                {
                    // Send chunk length (4 bytes, big-endian)
                    var lengthBytes = BitConverter.GetBytes(bytesRead);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(lengthBytes);
                    await networkStream.WriteAsync(lengthBytes, innerCt);

                    // Send chunk data
                    await networkStream.WriteAsync(buffer.AsMemory(0, bytesRead), innerCt);
                }

                // Send zero-length chunk to signal end of stream
                await networkStream.WriteAsync(new byte[4], innerCt);

                // Read response
                var responseBuffer = new byte[1024];
                var responseLength = await networkStream.ReadAsync(responseBuffer, innerCt);
                var response = Encoding.UTF8.GetString(responseBuffer, 0, responseLength).Trim('\0', '\n', '\r');

                // Reset stream position for subsequent use
                if (stream.CanSeek)
                    stream.Position = 0;

                return ParseResponse(response, fileName);
            }, ct);
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Failed to connect to ClamAV at {Host}:{Port} for file {FileName}",
                _host, _port, fileName);
            return MalwareScanResult.Failed($"Scanner unavailable: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Malware scan failed for file {FileName}", fileName);
            return MalwareScanResult.Failed($"Scan error: {ex.Message}");
        }
    }

    public async Task<MalwareScanResult> ScanAsync(byte[] data, string fileName, CancellationToken ct)
    {
        using var stream = new MemoryStream(data, writable: false);
        return await ScanAsync(stream, fileName, ct);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (!_enabled)
            return false;

        try
        {
            // No resilience pipeline for health checks — they should fail fast
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, ct);
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;

            await using var networkStream = client.GetStream();

            // Send PING command
            await networkStream.WriteAsync("zPING\0"u8.ToArray(), ct);

            // Read response
            var responseBuffer = new byte[64];
            var responseLength = await networkStream.ReadAsync(responseBuffer, ct);
            var response = Encoding.UTF8.GetString(responseBuffer, 0, responseLength).Trim('\0', '\n', '\r');

            return response == "PONG";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ClamAV health check failed");
            return false;
        }
    }

    private MalwareScanResult ParseResponse(string response, string fileName)
    {
        // Response format: "stream: OK" or "stream: <signature> FOUND"
        if (response.EndsWith("OK", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("File {FileName} is clean", fileName);
            return MalwareScanResult.Clean();
        }

        if (response.Contains("FOUND", StringComparison.OrdinalIgnoreCase))
        {
            // Extract threat name: "stream: Win.Test.EICAR_HDB-1 FOUND"
            var parts = response.Split(':');
            var threatPart = parts.Length > 1 ? parts[1].Trim() : response;
            var threatName = threatPart.Replace(" FOUND", "", StringComparison.OrdinalIgnoreCase).Trim();

            _logger.LogWarning("Malware detected in {FileName}: {ThreatName}", fileName, threatName);
            return MalwareScanResult.Infected(threatName);
        }

        if (response.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("ClamAV error scanning {FileName}: {Response}", fileName, response);
            return MalwareScanResult.Failed(response);
        }

        _logger.LogWarning("Unexpected ClamAV response for {FileName}: {Response}", fileName, response);
        return MalwareScanResult.Failed($"Unexpected response: {response}");
    }
}
