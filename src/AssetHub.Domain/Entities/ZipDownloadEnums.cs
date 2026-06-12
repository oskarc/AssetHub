namespace AssetHub.Domain.Entities;

/// <summary>
/// Status of a ZIP download build.
/// </summary>
public enum ZipDownloadStatus
{
    /// <summary>Fallback for unknown database values.</summary>
    Unknown = 0,
    Pending,
    Building,
    Completed,
    Failed
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the ZIP download enums.
/// </summary>
public static class ZipDownloadEnumExtensions
{
    public static string ToDbString(this ZipDownloadStatus status) => status switch
    {
        ZipDownloadStatus.Pending => "pending",
        ZipDownloadStatus.Building => "building",
        ZipDownloadStatus.Completed => "completed",
        ZipDownloadStatus.Failed => "failed",
        ZipDownloadStatus.Unknown => "unknown",
        _ => "unknown" // Fallback for future values
    };

    public static ZipDownloadStatus ToZipDownloadStatus(this string value) => value switch
    {
        "pending" => ZipDownloadStatus.Pending,
        "building" => ZipDownloadStatus.Building,
        "completed" => ZipDownloadStatus.Completed,
        "failed" => ZipDownloadStatus.Failed,
        _ => ZipDownloadStatus.Unknown // Graceful fallback for unknown database values
    };
}
