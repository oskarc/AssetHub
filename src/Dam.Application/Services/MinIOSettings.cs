namespace Dam.Application.Services;

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
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// MinIO access key (username).
    /// </summary>
    public string AccessKey { get; set; } = "";

    /// <summary>
    /// MinIO secret key (password).
    /// </summary>
    public string SecretKey { get; set; } = "";

    /// <summary>
    /// Name of the MinIO bucket to use for asset storage.
    /// </summary>
    public string BucketName { get; set; } = "";

    /// <summary>
    /// Whether to use SSL/TLS for MinIO connections. Must be true in production.
    /// </summary>
    public bool UseSSL { get; set; } = true;
}
