namespace AssetHub.Domain.Entities;

/// <summary>
/// Asset lifecycle status.
/// </summary>
public enum AssetStatus
{
    /// <summary>Fallback for unknown database values.</summary>
    Unknown = 0,
    Uploading,
    Processing,
    Ready,
    Failed
}

/// <summary>
/// Broad media type classification for an asset.
/// </summary>
public enum AssetType
{
    /// <summary>Fallback for unknown database values.</summary>
    Unknown = 0,
    Image,
    Video,
    Document
}

/// <summary>
/// Role assigned to a principal on a collection (ACL).
/// Ordered from least to most privilege.
/// </summary>
public enum AclRole
{
    Viewer = 1,
    Contributor = 2,
    Manager = 3,
    Admin = 4
}

/// <summary>
/// Scope of a share link.
/// </summary>
public enum ShareScopeType
{
    Asset,
    Collection
}

/// <summary>
/// Type of principal in an ACL entry.
/// </summary>
public enum PrincipalType
{
    User
}

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
/// Extension methods for enum ↔ string conversion.
/// These map between the new enums and existing lowercase string values
/// stored in the database, maintaining backward compatibility.
/// </summary>
public static class DomainEnumExtensions
{
    // ── AssetStatus ─────────────────────────────────────────────────────

    public static string ToDbString(this AssetStatus status) => status switch
    {
        AssetStatus.Uploading => "uploading",
        AssetStatus.Processing => "processing",
        AssetStatus.Ready => "ready",
        AssetStatus.Failed => "failed",
        AssetStatus.Unknown => "unknown",
        _ => "unknown" // Fallback for future values
    };

    public static AssetStatus ToAssetStatus(this string value) => value switch
    {
        "uploading" => AssetStatus.Uploading,
        "processing" => AssetStatus.Processing,
        "ready" => AssetStatus.Ready,
        "failed" => AssetStatus.Failed,
        _ => AssetStatus.Unknown // Graceful fallback for unknown database values
    };

    // ── AssetType ───────────────────────────────────────────────────────

    public static string ToDbString(this AssetType type) => type switch
    {
        AssetType.Image => "image",
        AssetType.Video => "video",
        AssetType.Document => "document",
        AssetType.Unknown => "unknown",
        _ => "unknown" // Fallback for future values
    };

    public static AssetType ToAssetType(this string value) => value switch
    {
        "image" => AssetType.Image,
        "video" => AssetType.Video,
        "document" => AssetType.Document,
        _ => AssetType.Unknown // Graceful fallback for unknown database values
    };

    // ── AclRole ─────────────────────────────────────────────────────────

    public static string ToDbString(this AclRole role) => role switch
    {
        AclRole.Viewer => "viewer",
        AclRole.Contributor => "contributor",
        AclRole.Manager => "manager",
        AclRole.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static AclRole ToAclRole(this string value) => value switch
    {
        "viewer" => AclRole.Viewer,
        "contributor" => AclRole.Contributor,
        "manager" => AclRole.Manager,
        "admin" => AclRole.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown ACL role: {value}")
    };

    // ── ShareScopeType ──────────────────────────────────────────────────

    public static string ToDbString(this ShareScopeType scope) => scope switch
    {
        ShareScopeType.Asset => "asset",
        ShareScopeType.Collection => "collection",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    public static ShareScopeType ToShareScopeType(this string value) => value switch
    {
        "asset" => ShareScopeType.Asset,
        "collection" => ShareScopeType.Collection,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown share scope type: {value}")
    };

    // ── PrincipalType ───────────────────────────────────────────────────

    public static string ToDbString(this PrincipalType type) => type switch
    {
        PrincipalType.User => "user",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static PrincipalType ToPrincipalType(this string value) => value switch
    {
        "user" => PrincipalType.User,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown principal type: {value}")
    };

    // ── ZipDownloadStatus ───────────────────────────────────────────────

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
