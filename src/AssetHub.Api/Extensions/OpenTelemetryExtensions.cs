using AssetHub.Application.Configuration;
using AssetHub.Infrastructure.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AssetHub.Api.Extensions;

/// <summary>
/// Configures OpenTelemetry for the API service.
/// Adds ASP.NET Core instrumentation on top of the shared configuration.
/// </summary>
public static class OpenTelemetryExtensions
{
    private static readonly string[] ExcludedTracePathPrefixes =
        ["/health", "/_blazor", "/_framework", "/css", "/js"];

    private static bool IsExcludedTracePath(Microsoft.AspNetCore.Http.PathString path)
    {
        foreach (var prefix in ExcludedTracePathPrefixes)
        {
            if (path.StartsWithSegments(prefix)) return true;
        }
        return false;
    }

    public static IServiceCollection AddAssetHubOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection(OpenTelemetrySettings.SectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        return services.AddSharedOpenTelemetry(
            configuration,
            serviceName: settings.ServiceName,
            configureTracing: tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(options =>
                {
                    // Filter out health checks, static files, and internal paths from traces
                    options.Filter = context => !IsExcludedTracePath(context.Request.Path);

                    options.RecordException = settings.RecordExceptions;

                    if (settings.StripQueryStrings)
                    {
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            // Overwrite url.query tag to prevent sensitive data leakage
                            activity.SetTag("url.query", null);
                        };
                    }
                });
            },
            configureMetrics: metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
            });
    }
}
