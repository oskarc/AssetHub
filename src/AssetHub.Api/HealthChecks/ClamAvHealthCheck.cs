using AssetHub.Application.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AssetHub.Api.HealthChecks;

/// <summary>
/// Verifies ClamAV connectivity by sending a PING command.
/// Returns Healthy when scanner is available, Degraded when disabled, Unhealthy when enabled but unreachable.
/// </summary>
internal sealed class ClamAvHealthCheck(IMalwareScannerService scanner, IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var enabled = config.GetValue("ClamAV:Enabled", false);
        
        if (!enabled)
            return HealthCheckResult.Healthy("ClamAV scanning is disabled.");

        try
        {
            var available = await scanner.IsAvailableAsync(cancellationToken);
            return available
                ? HealthCheckResult.Healthy("ClamAV is responding to PING.")
                : HealthCheckResult.Unhealthy("ClamAV is not responding.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ClamAV unreachable.", ex);
        }
    }
}
