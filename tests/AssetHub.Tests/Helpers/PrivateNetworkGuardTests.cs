using System.Net;
using AssetHub.Application.Helpers;

namespace AssetHub.Tests.Helpers;

/// <summary>
/// Range-classification tests for the private/internal address check used by the
/// forwarded-headers proxy-mismatch warning. The former SSRF surface
/// (URL validation and guarded connect) was removed in 2026-08 with the last
/// feature that fetched admin-supplied URLs; these cases cover what remains.
/// </summary>
public class PrivateNetworkGuardTests
{
    [Fact]
    public void IsPrivateOrInternal_Loopback_ReturnsTrue()
    {
        Assert.True(PrivateNetworkGuard.IsPrivateOrInternal(IPAddress.Loopback));
        Assert.True(PrivateNetworkGuard.IsPrivateOrInternal(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void IsPrivateOrInternal_CloudMetadata_ReturnsTrue()
    {
        // IMDS at 169.254.169.254 sits in the link-local range.
        Assert.True(PrivateNetworkGuard.IsPrivateOrInternal(IPAddress.Parse("169.254.169.254")));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    public void IsPrivateOrInternal_PrivateRanges_ReturnTrue(string address)
    {
        Assert.True(PrivateNetworkGuard.IsPrivateOrInternal(IPAddress.Parse(address)));
    }

    [Fact]
    public void IsPrivateOrInternal_IPv4MappedPrivate_ReturnsTrue()
    {
        // ::ffff:10.0.0.1 must normalise to its IPv4 form before the RFC 1918 check.
        Assert.True(PrivateNetworkGuard.IsPrivateOrInternal(IPAddress.Parse("::ffff:10.0.0.1")));
    }

    [Fact]
    public void IsPrivateOrInternal_PublicAddress_ReturnsFalse()
    {
        Assert.False(PrivateNetworkGuard.IsPrivateOrInternal(IPAddress.Parse("1.1.1.1")));
        Assert.False(PrivateNetworkGuard.IsPrivateOrInternal(IPAddress.Parse("8.8.8.8")));
    }
}
