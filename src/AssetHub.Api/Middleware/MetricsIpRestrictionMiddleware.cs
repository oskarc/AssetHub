using AssetHub.Infrastructure.DependencyInjection;

namespace AssetHub.Api.Middleware;

/// <summary>
/// Rejects requests to /metrics from non-private IPs.
/// Defense-in-depth: even if a reverse proxy forwards this path, the application
/// itself refuses to serve metrics to the public internet.
/// </summary>
public sealed class MetricsIpRestrictionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MetricsIpRestrictionMiddleware> _logger;

    public MetricsIpRestrictionMiddleware(RequestDelegate next, ILogger<MetricsIpRestrictionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/metrics"))
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp is null || !OpenTelemetryServiceExtensions.IsPrivateIp(remoteIp))
            {
                _logger.LogWarning("Rejected /metrics request from non-private IP {RemoteIp}", remoteIp);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await _next(context);
    }
}
