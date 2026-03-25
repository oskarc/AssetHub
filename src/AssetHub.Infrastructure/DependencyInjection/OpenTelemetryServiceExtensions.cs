using System.Net;
using System.Reflection;
using AssetHub.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AssetHub.Infrastructure.DependencyInjection;

/// <summary>
/// Shared OpenTelemetry configuration used by both the API and Worker services.
/// Handles resource identification, OTLP export, sampling, and transport validation.
/// </summary>
public static class OpenTelemetryServiceExtensions
{
    /// <summary>
    /// Adds shared OpenTelemetry tracing and metrics. Host-specific instrumentation
    /// (e.g., ASP.NET Core) can be added via the optional callbacks.
    /// </summary>
    public static IServiceCollection AddSharedOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        var settings = configuration.GetSection(OpenTelemetrySettings.SectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        if (!settings.Enabled)
            return services;

        // Validate OTLP transport security at startup via hosted service
        services.AddHostedService<OtlpTransportValidationService>();

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: serviceName,
                serviceVersion: Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                    ?? "Production"
            });

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = settings.RecordExceptions;

                        if (settings.StripQueryStrings)
                        {
                            options.EnrichWithHttpRequestMessage = (activity, message) =>
                            {
                                if (message.RequestUri is { } uri)
                                    activity.SetTag("url.full", uri.GetLeftPart(UriPartial.Path));
                            };
                        }
                    })
                    .AddEntityFrameworkCoreInstrumentation();

                if (settings.SamplingRatio < 1.0)
                    tracing.SetSampler(new TraceIdRatioBasedSampler(settings.SamplingRatio));

                if (!string.IsNullOrWhiteSpace(settings.OtlpEndpoint))
                    tracing.AddOtlpExporter(options => ConfigureOtlpTraceExporter(options, settings));

                configureTracing?.Invoke(tracing);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation();

                if (!string.IsNullOrWhiteSpace(settings.OtlpEndpoint))
                    metrics.AddOtlpExporter(options => ConfigureOtlpMetricExporter(options, settings));

                configureMetrics?.Invoke(metrics);
            });

        return services;
    }

    /// <summary>
    /// Returns true if the address belongs to a private/loopback range (RFC 1918 + loopback).
    /// Used by OTLP transport validation to detect cleartext exports over public networks.
    /// </summary>
    internal static bool IsPrivateIp(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.Equals(IPAddress.IPv6Loopback))
            return true;

        // Handle IPv4-mapped IPv6 addresses (e.g., ::ffff:172.19.0.5 from Docker)
        var checkAddress = address;
        if (address.IsIPv4MappedToIPv6)
        {
            checkAddress = address.MapToIPv4();
        }

        var bytes = checkAddress.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] switch
            {
                10 => true,                                            // 10.0.0.0/8
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,    // 172.16.0.0/12
                192 when bytes[1] == 168 => true,                     // 192.168.0.0/16
                127 => true,                                           // 127.0.0.0/8 (redundant with IsLoopback, but explicit)
                _ => false
            };
        }

        return false;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void ConfigureOtlpTraceExporter(OtlpExporterOptions options, OpenTelemetrySettings settings)
    {
        options.Endpoint = new Uri(settings.OtlpEndpoint);
        options.TimeoutMilliseconds = settings.ExportTimeoutMs;
        options.BatchExportProcessorOptions = new BatchExportProcessorOptions<System.Diagnostics.Activity>
        {
            MaxQueueSize = 2048,
            MaxExportBatchSize = settings.BatchSize,
            ScheduledDelayMilliseconds = 5000
        };
        ApplyAuthHeader(options, settings);
    }

    private static void ConfigureOtlpMetricExporter(OtlpExporterOptions options, OpenTelemetrySettings settings)
    {
        options.Endpoint = new Uri(settings.OtlpEndpoint);
        options.TimeoutMilliseconds = settings.ExportTimeoutMs;
        ApplyAuthHeader(options, settings);
    }

    private static void ApplyAuthHeader(OtlpExporterOptions options, OpenTelemetrySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OtlpAuthHeader))
            return;

        var parts = settings.OtlpAuthHeader.Split('=', 2);
        if (parts.Length == 2)
            options.Headers = $"{parts[0]}={parts[1]}";
    }
}

/// <summary>
/// Validates at startup that the OTLP endpoint is not transmitting trace data
/// in cleartext over a non-private network. Logs a warning (or critical in Production).
/// </summary>
internal sealed class OtlpTransportValidationService : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OtlpTransportValidationService> _logger;

    public OtlpTransportValidationService(
        IConfiguration configuration,
        ILogger<OtlpTransportValidationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = _configuration.GetSection(OpenTelemetrySettings.SectionName)
            .Get<OpenTelemetrySettings>() ?? new OpenTelemetrySettings();

        if (string.IsNullOrWhiteSpace(settings.OtlpEndpoint))
            return Task.CompletedTask;

        try
        {
            var uri = new Uri(settings.OtlpEndpoint);

            // TLS is in use — no concern
            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            var addresses = Dns.GetHostAddresses(uri.Host);
            if (addresses.All(OpenTelemetryServiceExtensions.IsPrivateIp))
                return Task.CompletedTask;

            // Non-TLS endpoint resolving to a non-private IP
            var env = _configuration["ASPNETCORE_ENVIRONMENT"]
                   ?? _configuration["DOTNET_ENVIRONMENT"]
                   ?? "Production";

            const string message =
                "OTLP endpoint uses HTTP (no TLS) and resolves to a non-private IP. " +
                "Trace and metric data may be transmitted in cleartext. " +
                "Use HTTPS or ensure the endpoint is on a private network.";

            if (env.Equals("Production", StringComparison.OrdinalIgnoreCase))
                _logger.LogCritical("OTLP transport security violation: {Message} Endpoint: {Endpoint}", message, settings.OtlpEndpoint);
            else
                _logger.LogWarning("OTLP transport security warning: {Message} Endpoint: {Endpoint}", message, settings.OtlpEndpoint);
        }
        catch (Exception ex)
        {
            // DNS resolution failure at startup should not prevent the app from starting
            _logger.LogWarning(ex, "Could not validate OTLP endpoint transport security for {Endpoint}", settings.OtlpEndpoint);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
