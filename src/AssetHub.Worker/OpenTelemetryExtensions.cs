using AssetHub.Application.Configuration;
using AssetHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Worker;

/// <summary>
/// Configures OpenTelemetry for the Worker service.
/// Uses the shared configuration; no additional instrumentation needed for background jobs.
/// </summary>
public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddWorkerOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection(OpenTelemetrySettings.SectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        return services.AddSharedOpenTelemetry(
            configuration,
            serviceName: $"{settings.ServiceName}.Worker");
    }
}
