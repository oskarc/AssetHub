using System.Net;
using System.Net.Sockets;

namespace AssetHub.Application.Helpers;

/// <summary>
/// SSRF defense for caller-supplied URLs that AssetHub will fetch on behalf of
/// admins (webhooks, S3 migration sources, future integrations). Rejects URLs
/// whose host resolves to loopback / link-local / private / cloud-metadata
/// ranges so a compromised admin can't probe internal services or exfiltrate
/// IMDS credentials by registering a webhook against
/// <c>http://169.254.169.254/...</c>.
/// </summary>
/// <remarks>
/// <para>
/// The check runs twice: once at registration time (cheap rejection of
/// obviously bad URLs) and once at dispatch time. The dispatch-time check is
/// the load-bearing one — DNS rebinding can flip a public-resolving hostname
/// to a private IP between registration and dispatch.
/// </para>
/// <para>
/// Validation is intentionally loud: enumerated, hard-coded ranges. Adding a
/// "trusted internal host" allow-list would re-open the SSRF surface; if a
/// caller genuinely needs to webhook an internal service, they can register
/// against the public DNS name of the same service.
/// </para>
/// </remarks>
public static class OutboundUrlGuard
{
    /// <summary>
    /// Validates the URL is absolute, http(s), and resolves to a public IP.
    /// </summary>
    /// <param name="raw">The user-supplied URL string.</param>
    /// <param name="error">
    /// On failure: a short, non-revealing reason. Doesn't echo the resolved IP
    /// or scheme — the caller already knows what they sent.
    /// </param>
    /// <returns>True if safe to fetch; false otherwise.</returns>
    public static bool IsSafeOutboundUrl(string? raw, out string? error)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            error = "URL must be an absolute URL.";
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            error = "Only http and https schemes are accepted.";
            return false;
        }

        IPAddress[] addresses;
        try
        {
            // If the host is already an IP literal, GetHostAddresses returns
            // it without a DNS lookup. For names, this resolves via the
            // configured resolver — same one HttpClient will use later.
            addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
        }
        catch (SocketException)
        {
            error = "URL host could not be resolved.";
            return false;
        }
        catch (ArgumentException)
        {
            error = "URL host is malformed.";
            return false;
        }

        if (addresses.Length == 0)
        {
            error = "URL host did not resolve to any address.";
            return false;
        }

        if (addresses.Any(IsPrivateOrInternal))
        {
            error = "URL must point to a public address — private, loopback, and link-local ranges are not allowed.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Returns true if the supplied address is in a range that should never
    /// be the destination of an outbound integration request.
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
