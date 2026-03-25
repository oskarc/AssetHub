namespace AssetHub.Application.Configuration;

/// <summary>
/// OpenTelemetry configuration settings.
/// Bound to the "OpenTelemetry" section in appsettings.
/// </summary>
public class OpenTelemetrySettings
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// Whether OpenTelemetry is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Service name for traces and metrics.
    /// </summary>
    public string ServiceName { get; set; } = "AssetHub";

    /// <summary>
    /// OTLP endpoint (e.g., "http://aspire-dashboard:18889").
    /// Uses gRPC protocol. Leave empty to disable OTLP export.
    /// </summary>
    public string OtlpEndpoint { get; set; } = "";

    /// <summary>
    /// Optional OTLP authentication header value (e.g., API key for cloud providers).
    /// Format: "header-name=header-value" or leave empty for no auth.
    /// </summary>
    public string OtlpAuthHeader { get; set; } = "";

    /// <summary>
    /// Sampling ratio (0.0 to 1.0). Use 1.0 for development, lower for production.
    /// </summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>
    /// Maximum batch size for trace export. Default 512.
    /// </summary>
    public int BatchSize { get; set; } = 512;

    /// <summary>
    /// Export timeout in milliseconds. Default 30000 (30s).
    /// </summary>
    public int ExportTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Whether to record full exception details (stack traces) in traces.
    /// Set to false in production to avoid leaking sensitive information.
    /// </summary>
    public bool RecordExceptions { get; set; } = true;

    /// <summary>
    /// Whether to strip query strings from HTTP URLs in traces.
    /// Set to true in production to prevent sensitive data leakage (tokens, API keys).
    /// </summary>
    public bool StripQueryStrings { get; set; } = false;
}
