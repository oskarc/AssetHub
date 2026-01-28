namespace Dam.Application.Services;

/// <summary>
/// Configuration settings for MinIO object storage.
/// </summary>
public class MinIOSettings
{
    public const string SectionName = "MinIO";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "";
    public string BucketName { get; set; } = "assethub-dev";
    public bool UseSSL { get; set; } = false;
}
