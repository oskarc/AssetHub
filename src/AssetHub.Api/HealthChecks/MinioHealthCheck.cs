using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;

namespace AssetHub.Api.HealthChecks;

/// <summary>
/// Verifies MinIO connectivity by checking if the configured bucket exists.
/// </summary>
internal sealed class MinioHealthCheck(IMinioClient minio, IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var bucket = config["MinIO:BucketName"] ?? "assethub";
            var exists = await minio.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucket), cancellationToken);
            return exists
                ? HealthCheckResult.Healthy($"Bucket '{bucket}' is accessible.")
                : HealthCheckResult.Degraded($"Bucket '{bucket}' does not exist.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MinIO unreachable.", ex);
        }
    }
}
