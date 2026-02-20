using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Configuration settings for MinIO object storage.
/// Bound to the "MinIO" section in appsettings.
/// </summary>
public class MinIOSettings
{
    public const string SectionName = "MinIO";

    /// <summary>
    /// MinIO server endpoint (e.g. "minio.internal:9000").
    /// </summary>
    [Required]
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// MinIO access key (username).
    /// </summary>
    [Required]
    public string AccessKey { get; set; } = "";

    /// <summary>
    /// MinIO secret key (password).
    /// </summary>
    [Required]
    public string SecretKey { get; set; } = "";

    /// <summary>
    /// Name of the MinIO bucket to use for asset storage.
    /// </summary>
    [Required]
    public string BucketName { get; set; } = "";

    /// <summary>
    /// Whether to use SSL/TLS for MinIO connections. Must be true in production.
    /// </summary>
    public bool UseSSL { get; set; } = true;

    /// <summary>
    /// Public-facing MinIO endpoint that browsers can reach for presigned URL operations.
    /// Required when the internal Endpoint (e.g. "minio:9000" in Docker) differs from
    /// what the browser sees (e.g. "localhost:9000"). If not set, falls back to Endpoint.
    /// Format: "host:port" (without scheme — scheme is determined by PublicUseSSL).
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Whether presigned (public-facing) URLs should use HTTPS.
    /// Only relevant when PublicUrl is set. Defaults to same as UseSSL.
    /// </summary>
    public bool? PublicUseSSL { get; set; }
}
