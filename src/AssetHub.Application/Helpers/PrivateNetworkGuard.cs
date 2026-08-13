using System.Net;
using System.Net.Sockets;

namespace AssetHub.Application.Helpers;

/// <summary>
/// Classifies an IP address as private / internal. Used by the forwarded-headers
/// middleware to detect a reverse-proxy misconfiguration: when a connection's
/// remote address falls outside the trusted private ranges, the request either
/// bypassed the proxy or the proxy network isn't in <c>KnownNetworks</c>, and
/// every audit IP downstream would record the proxy instead of the client.
/// </summary>
/// <remarks>
/// This is the surviving half of the former <c>OutboundUrlGuard</c>, whose
/// SSRF-validation surface (URL checking and guarded connect callbacks) was
/// removed in 2026-08 along with the last feature that fetched from
/// admin-supplied URLs. The range classification below is unchanged; if a future
/// feature fetches a caller-supplied URL again, it needs the URL-level guard
/// back — this type alone is not an SSRF defence.
/// </remarks>
public static class PrivateNetworkGuard
{
    /// <summary>
    /// Returns true if the supplied address is in a loopback, private,
    /// link-local, or otherwise non-public range.
    /// </summary>
    public static bool IsPrivateOrInternal(IPAddress address)
    {
        // Normalise IPv4-mapped IPv6 (::ffff:1.2.3.4) to its IPv4 form so the
        // RFC 1918 check below catches it. Without this, a v4-mapped private
        // address slips through.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 (link-local + cloud metadata at 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 100.64.0.0/10 — carrier-grade NAT shared address space (RFC 6598)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
            // 0.0.0.0/8 — "this network" reserved + Linux's "any address"
            if (bytes[0] == 0) return true;
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv6 loopback already caught by IsLoopback above.
            // fc00::/7 — Unique Local Addresses (RFC 4193).
            if (address.IsIPv6UniqueLocal) return true;
            // fe80::/10 — link-local.
            if (address.IsIPv6LinkLocal) return true;
            // fec0::/10 — site-local (deprecated but still rejected for safety).
            if (address.IsIPv6SiteLocal) return true;
            return false;
        }

        // Unknown address family — fail closed.
        return true;
    }
}
