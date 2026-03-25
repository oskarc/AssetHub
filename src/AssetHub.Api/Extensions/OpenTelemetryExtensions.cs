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
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health") &&
                        !context.Request.Path.StartsWithSegments("/_blazor") &&
                        !context.Request.Path.StartsWithSegments("/_framework") &&
                        !context.Request.Path.StartsWithSegments("/css") &&
                        !context.Request.Path.StartsWithSegments("/js");

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
