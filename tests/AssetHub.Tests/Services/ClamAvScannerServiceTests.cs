using AssetHub.Application.Services;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit tests for ClamAvScannerService.
/// Tests configuration handling, response parsing, and disabled scanner behavior.
/// </summary>
public class ClamAvScannerServiceTests
{
    private static IConfiguration CreateConfig(bool enabled = false, string host = "localhost", int port = 3310)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ClamAV:Enabled"] = enabled.ToString(),
            ["ClamAV:Host"] = host,
            ["ClamAV:Port"] = port.ToString(),
            ["ClamAV:TimeoutMs"] = "5000",
            ["ClamAV:ChunkSize"] = "4096"
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
    }

    [Fact]
    public async Task ScanAsync_WhenDisabled_ReturnsSkipped()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        var scanner = new ClamAvScannerService(config, NullLogger<ClamAvScannerService>.Instance);
        using var stream = new MemoryStream("test content"u8.ToArray());

        // Act
        var result = await scanner.ScanAsync(stream, "test.txt", CancellationToken.None);

        // Assert
        Assert.True(result.ScanCompleted);
        Assert.True(result.IsClean);
        Assert.Contains("disabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_ByteArray_WhenDisabled_ReturnsSkipped()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        var scanner = new ClamAvScannerService(config, NullLogger<ClamAvScannerService>.Instance);
        var data = "test content"u8.ToArray();

        // Act
        var result = await scanner.ScanAsync(data, "test.txt", CancellationToken.None);

        // Assert
        Assert.True(result.ScanCompleted);
        Assert.True(result.IsClean);
    }

    [Fact]
    public async Task IsAvailableAsync_WhenDisabled_ReturnsFalse()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        var scanner = new ClamAvScannerService(config, NullLogger<ClamAvScannerService>.Instance);

        // Act
        var result = await scanner.IsAvailableAsync(CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ScanAsync_WhenEnabled_ButHostUnreachable_ReturnsFailed()
    {
        // Arrange - use localhost on a port unlikely to have ClamAV
        var config = CreateConfig(enabled: true, host: "127.0.0.1", port: 59999);
        var scanner = new ClamAvScannerService(config, NullLogger<ClamAvScannerService>.Instance);
        using var stream = new MemoryStream("test content"u8.ToArray());

        // Act
        var result = await scanner.ScanAsync(stream, "test.txt", CancellationToken.None);

        // Assert
        Assert.False(result.ScanCompleted);
        Assert.Null(result.IsClean);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("unavailable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IsAvailableAsync_WhenEnabled_ButHostUnreachable_ReturnsFalse()
    {
        // Arrange
        var config = CreateConfig(enabled: true, host: "127.0.0.1", port: 59999);
        var scanner = new ClamAvScannerService(config, NullLogger<ClamAvScannerService>.Instance);

        // Act
        var result = await scanner.IsAvailableAsync(CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MalwareScanResult_Clean_HasCorrectProperties()
    {
        // Act
        var result = MalwareScanResult.Clean();

        // Assert
        Assert.True(result.ScanCompleted);
        Assert.True(result.IsClean);
        Assert.Null(result.ThreatName);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void MalwareScanResult_Infected_HasCorrectProperties()
    {
        // Act
        var result = MalwareScanResult.Infected("Win.Test.EICAR_HDB-1");

        // Assert
        Assert.True(result.ScanCompleted);
        Assert.False(result.IsClean);
        Assert.Equal("Win.Test.EICAR_HDB-1", result.ThreatName);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void MalwareScanResult_Failed_HasCorrectProperties()
    {
        // Act
        var result = MalwareScanResult.Failed("Connection refused");

        // Assert
        Assert.False(result.ScanCompleted);
        Assert.Null(result.IsClean);
        Assert.Null(result.ThreatName);
        Assert.Equal("Connection refused", result.ErrorMessage);
    }

    [Fact]
    public void MalwareScanResult_Skipped_HasCorrectProperties()
    {
        // Act
        var result = MalwareScanResult.Skipped();

        // Assert
        Assert.True(result.ScanCompleted);
        Assert.True(result.IsClean);
        Assert.Null(result.ThreatName);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Constructor_WithMissingConfig_UsesDefaults()
    {
        // Arrange - empty config
        var config = new ConfigurationBuilder().Build();
        
        // Act - should not throw
        var scanner = new ClamAvScannerService(config, NullLogger<ClamAvScannerService>.Instance);

        // Assert - scanner is disabled by default
        var result = await scanner.IsAvailableAsync(CancellationToken.None);
        Assert.False(result);
    }
}
