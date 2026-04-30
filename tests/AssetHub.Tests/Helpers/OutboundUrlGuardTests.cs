using System.Net;
using AssetHub.Application.Helpers;

namespace AssetHub.Tests.Helpers;

/// <summary>
/// Unit tests for the SSRF / DNS-rebinding guard. The registration-time
/// <c>IsSafeOutboundUrl</c> path is exercised through the webhook + S3
/// connector tests; this file focuses on the dispatch-time
/// <c>ConnectGuardedAsync</c> path that closes the rebind window.
/// </summary>
public class OutboundUrlGuardTests
{
    [Fact]
    public void IsPrivateOrInternal_Loopback_ReturnsTrue()
    {
        Assert.True(OutboundUrlGuard.IsPrivateOrInternal(IPAddress.Loopback));
        Assert.True(OutboundUrlGuard.IsPrivateOrInternal(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void IsPrivateOrInternal_CloudMetadata_ReturnsTrue()
    {
        // IMDS at 169.254.169.254 is the canonical SSRF target.
        Assert.True(OutboundUrlGuard.IsPrivateOrInternal(IPAddress.Parse("169.254.169.254")));
    }

    [Fact]
    public void IsPrivateOrInternal_PublicAddress_ReturnsFalse()
    {
        Assert.False(OutboundUrlGuard.IsPrivateOrInternal(IPAddress.Parse("1.1.1.1")));
        Assert.False(OutboundUrlGuard.IsPrivateOrInternal(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public async Task ConnectGuardedAsync_LocalhostHost_ThrowsWithoutOpeningSocket()
    {
        // localhost always resolves to 127.0.0.1 (or ::1) — both are loopback.
        // The guard must reject before any TCP dial. Port 1 is intentionally
        // privileged + closed: if the guard ever regresses to "connect first,
        // ask later", we'd see a SocketException, not the IOException below.
        var ex = await Assert.ThrowsAsync<IOException>(
            async () => await OutboundUrlGuard.ConnectGuardedAsync("localhost", port: 1, ct: CancellationToken.None));

        Assert.Contains("non-public", ex.Message);
    }

    [Fact]
    public async Task ConnectGuardedAsync_IpLiteralPrivate_ThrowsWithoutOpeningSocket()
    {
        // 10.0.0.1 is RFC 1918 — must be refused even when supplied as a
        // literal (no DNS rebinding window, just a directly-asserted private IP).
        var ex = await Assert.ThrowsAsync<IOException>(
            async () => await OutboundUrlGuard.ConnectGuardedAsync("10.0.0.1", port: 1, ct: CancellationToken.None));

        Assert.Contains("non-public", ex.Message);
    }

    [Fact]
    public async Task ConnectGuardedAsync_CloudMetadataIpLiteral_ThrowsWithoutOpeningSocket()
    {
        // The IMDS literal — most direct expression of the threat we close.
        var ex = await Assert.ThrowsAsync<IOException>(
            async () => await OutboundUrlGuard.ConnectGuardedAsync("169.254.169.254", port: 80, ct: CancellationToken.None));

        Assert.Contains("non-public", ex.Message);
    }

    [Fact]
    public async Task ConnectGuardedAsync_UnresolvableHost_ThrowsWrappedSocketException()
    {
        // .invalid is RFC 2606 reserved — guaranteed never to resolve.
        // We surface IOException so callers can pattern-match the same way
        // for "rejected" and "couldn't resolve" — both end the dispatch.
        var ex = await Assert.ThrowsAsync<IOException>(
            async () => await OutboundUrlGuard.ConnectGuardedAsync("definitely-not-real.invalid", port: 80, ct: CancellationToken.None));

        Assert.Contains("could not be resolved", ex.Message);
    }
}
