using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AssetHub.Api.HealthChecks;

/// <summary>
/// Verifies Keycloak is reachable by fetching the OIDC discovery document.
/// </summary>
internal sealed class KeycloakHealthCheck(
    IConfiguration config, IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var authority = config["Keycloak:Authority"];
            if (string.IsNullOrWhiteSpace(authority))
                return HealthCheckResult.Degraded("Keycloak:Authority not configured.");

            var discoveryUrl = authority.TrimEnd('/') + "/.well-known/openid-configuration";
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync(discoveryUrl, ct);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy(
                    $"OIDC discovery endpoint returned {(int)response.StatusCode}.")
                : HealthCheckResult.Degraded(
                    $"OIDC discovery returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Keycloak unreachable.", ex);
        }
    }
}
