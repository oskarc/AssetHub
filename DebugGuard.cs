using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace AssetHub;

public static class DebugGuard
{
    public static bool IsLocalDebugRequest(HttpContext http)
    {
        // If user is accessing via localhost, allow regardless of Docker bridge source IP.
        var host = http.Request.Host.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
            return true;

        var ip = http.Connection.RemoteIpAddress;
        if (ip is null)
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            // RFC1918: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
            if (b[0] == 10)
                return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                return true;
            if (b[0] == 192 && b[1] == 168)
                return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;

            // Unique local addresses: fc00::/7
            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
