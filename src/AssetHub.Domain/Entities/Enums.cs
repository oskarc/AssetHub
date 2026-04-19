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
/// Source type for a bulk import migration.
/// </summary>
public enum MigrationSourceType
{
    CsvUpload
}

/// <summary>
/// Overall status of a migration job.
/// </summary>
public enum MigrationStatus
{
    Draft,
    Validating,
    Running,
    Completed,
    PartiallyCompleted,
    CompletedWithErrors,
    Failed,
    Cancelled
}

/// <summary>
/// Status of an individual migration item.
/// </summary>
public enum MigrationItemStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Skipped
}

/// <summary>
/// How a preset resizes the source image to fit the target dimensions.
/// </summary>
public enum ExportPresetFitMode
{
    /// <summary>Scale to fit within the target box, preserving aspect ratio.</summary>
    Contain,
    /// <summary>Scale to cover the target box, cropping excess.</summary>
    Cover,
    /// <summary>Stretch to exact target dimensions, ignoring aspect ratio.</summary>
    Stretch,
    /// <summary>Scale to target width, height determined by aspect ratio.</summary>
    Width,
    /// <summary>Scale to target height, width determined by aspect ratio.</summary>
    Height
}

/// <summary>
/// Output image format for an export preset.
/// </summary>
public enum ExportPresetFormat
{
    /// <summary>Keep the same format as the source image.</summary>
    Original,
    Jpeg,
    Png,
    WebP
}

/// <summary>
/// Scope of a metadata schema — determines which assets it applies to.
/// </summary>
public enum MetadataSchemaScope
{
    Global,
    AssetType,
    Collection
}

/// <summary>
/// Data type for a metadata field.
/// </summary>
public enum MetadataFieldType
{
    Text,
    LongText,
    Number,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Select,
    MultiSelect,
    Taxonomy,
    Url
}

/// <summary>
/// Extension methods for enum ↔ string conversion.
/// These map between the new enums and existing lowercase string values
/// stored in the database, maintaining backward compatibility.
/// </summary>
public static class DomainEnumExtensions
{
    private const string Failed = "failed";
    private const string Unknown = "unknown";

    // ── ToDbString overloads ────────────────────────────────────────────

    public static string ToDbString(this AssetStatus status) => status switch
    {
        AssetStatus.Uploading => "uploading",
        AssetStatus.Processing => "processing",
        AssetStatus.Ready => "ready",
        AssetStatus.Failed => Failed,
        AssetStatus.Unknown => Unknown,
        _ => Unknown // Fallback for future values
    };

    public static string ToDbString(this AssetType type) => type switch
    {
        AssetType.Image => "image",
        AssetType.Video => "video",
        AssetType.Document => "document",
        AssetType.Unknown => Unknown,
        _ => Unknown // Fallback for future values
    };

    public static string ToDbString(this AclRole role) => role switch
    {
        AclRole.Viewer => "viewer",
        AclRole.Contributor => "contributor",
        AclRole.Manager => "manager",
        AclRole.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static string ToDbString(this ShareScopeType scope) => scope switch
    {
        ShareScopeType.Asset => "asset",
        ShareScopeType.Collection => "collection",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    public static string ToDbString(this PrincipalType type) => type switch
    {
        PrincipalType.User => "user",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static string ToDbString(this ZipDownloadStatus status) => status switch
    {
        ZipDownloadStatus.Pending => "pending",
        ZipDownloadStatus.Building => "building",
        ZipDownloadStatus.Completed => "completed",
        ZipDownloadStatus.Failed => Failed,
        ZipDownloadStatus.Unknown => Unknown,
        _ => Unknown // Fallback for future values
    };

    public static string ToDbString(this MigrationSourceType type) => type switch
    {
        MigrationSourceType.CsvUpload => "csv_upload",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static string ToDbString(this MigrationStatus status) => status switch
    {
        MigrationStatus.Draft => "draft",
        MigrationStatus.Validating => "validating",
        MigrationStatus.Running => "running",
        MigrationStatus.Completed => "completed",
        MigrationStatus.PartiallyCompleted => "partially_completed",
        MigrationStatus.CompletedWithErrors => "completed_with_errors",
        MigrationStatus.Failed => Failed,
        MigrationStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static string ToDbString(this MigrationItemStatus status) => status switch
    {
        MigrationItemStatus.Pending => "pending",
        MigrationItemStatus.Processing => "processing",
        MigrationItemStatus.Succeeded => "succeeded",
        MigrationItemStatus.Failed => Failed,
        MigrationItemStatus.Skipped => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static string ToDbString(this ExportPresetFitMode mode) => mode switch
    {
        ExportPresetFitMode.Contain => "contain",
        ExportPresetFitMode.Cover => "cover",
        ExportPresetFitMode.Stretch => "stretch",
        ExportPresetFitMode.Width => "width",
        ExportPresetFitMode.Height => "height",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static string ToDbString(this ExportPresetFormat format) => format switch
    {
        ExportPresetFormat.Original => "original",
        ExportPresetFormat.Jpeg => "jpeg",
        ExportPresetFormat.Png => "png",
        ExportPresetFormat.WebP => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string ToDbString(this MetadataSchemaScope scope) => scope switch
    {
        MetadataSchemaScope.Global => "global",
        MetadataSchemaScope.AssetType => "asset_type",
        MetadataSchemaScope.Collection => "collection",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    public static string ToDbString(this MetadataFieldType type) => type switch
    {
        MetadataFieldType.Text => "text",
        MetadataFieldType.LongText => "long_text",
        MetadataFieldType.Number => "number",
        MetadataFieldType.Decimal => "decimal",
        MetadataFieldType.Boolean => "boolean",
        MetadataFieldType.Date => "date",
        MetadataFieldType.DateTime => "date_time",
        MetadataFieldType.Select => "select",
        MetadataFieldType.MultiSelect => "multi_select",
        MetadataFieldType.Taxonomy => "taxonomy",
        MetadataFieldType.Url => "url",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    // ── String → Enum conversions ───────────────────────────────────────

    public static AssetStatus ToAssetStatus(this string value) => value switch
    {
        "uploading" => AssetStatus.Uploading,
        "processing" => AssetStatus.Processing,
        "ready" => AssetStatus.Ready,
        Failed => AssetStatus.Failed,
        _ => AssetStatus.Unknown // Graceful fallback for unknown database values
    };

    public static AssetType ToAssetType(this string value) => value switch
    {
        "image" => AssetType.Image,
        "video" => AssetType.Video,
        "document" => AssetType.Document,
        _ => AssetType.Unknown // Graceful fallback for unknown database values
    };

    public static AclRole ToAclRole(this string value) => value switch
    {
        "viewer" => AclRole.Viewer,
        "contributor" => AclRole.Contributor,
        "manager" => AclRole.Manager,
        "admin" => AclRole.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown ACL role: {value}")
    };

    public static ShareScopeType ToShareScopeType(this string value) => value switch
    {
        "asset" => ShareScopeType.Asset,
        "collection" => ShareScopeType.Collection,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown share scope type: {value}")
    };

    public static PrincipalType ToPrincipalType(this string value) => value switch
    {
        "user" => PrincipalType.User,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown principal type: {value}")
    };

    public static ZipDownloadStatus ToZipDownloadStatus(this string value) => value switch
    {
        "pending" => ZipDownloadStatus.Pending,
        "building" => ZipDownloadStatus.Building,
        "completed" => ZipDownloadStatus.Completed,
        Failed => ZipDownloadStatus.Failed,
        _ => ZipDownloadStatus.Unknown // Graceful fallback for unknown database values
    };

    public static MigrationSourceType ToMigrationSourceType(this string value) => value switch
    {
        "csv_upload" => MigrationSourceType.CsvUpload,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown migration source type: {value}")
    };

    public static MigrationStatus ToMigrationStatus(this string value) => value switch
    {
        "draft" => MigrationStatus.Draft,
        "validating" => MigrationStatus.Validating,
        "running" => MigrationStatus.Running,
        "completed" => MigrationStatus.Completed,
        "partially_completed" => MigrationStatus.PartiallyCompleted,
        "completed_with_errors" => MigrationStatus.CompletedWithErrors,
        Failed => MigrationStatus.Failed,
        "cancelled" => MigrationStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown migration status: {value}")
    };

    public static MigrationItemStatus ToMigrationItemStatus(this string value) => value switch
    {
        "pending" => MigrationItemStatus.Pending,
        "processing" => MigrationItemStatus.Processing,
        "succeeded" => MigrationItemStatus.Succeeded,
        Failed => MigrationItemStatus.Failed,
        "skipped" => MigrationItemStatus.Skipped,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown migration item status: {value}")
    };

    public static ExportPresetFitMode ToExportPresetFitMode(this string value) => value switch
    {
        "contain" => ExportPresetFitMode.Contain,
        "cover" => ExportPresetFitMode.Cover,
        "stretch" => ExportPresetFitMode.Stretch,
        "width" => ExportPresetFitMode.Width,
        "height" => ExportPresetFitMode.Height,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown export preset fit mode: {value}")
    };

    public static ExportPresetFormat ToExportPresetFormat(this string value) => value switch
    {
        "original" => ExportPresetFormat.Original,
        "jpeg" => ExportPresetFormat.Jpeg,
        "png" => ExportPresetFormat.Png,
        "webp" => ExportPresetFormat.WebP,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown export preset format: {value}")
    };

    public static bool IsValidExportPresetFitMode(string value) => value is "contain" or "cover" or "stretch" or "width" or "height";

    public static bool IsValidExportPresetFormat(string value) => value is "original" or "jpeg" or "png" or "webp";

    public static MetadataSchemaScope ToMetadataSchemaScope(this string value) => value switch
    {
        "global" => MetadataSchemaScope.Global,
        "asset_type" => MetadataSchemaScope.AssetType,
        "collection" => MetadataSchemaScope.Collection,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown metadata schema scope: {value}")
    };

    public static MetadataFieldType ToMetadataFieldType(this string value) => value switch
    {
        "text" => MetadataFieldType.Text,
        "long_text" => MetadataFieldType.LongText,
        "number" => MetadataFieldType.Number,
        "decimal" => MetadataFieldType.Decimal,
        "boolean" => MetadataFieldType.Boolean,
        "date" => MetadataFieldType.Date,
        "date_time" => MetadataFieldType.DateTime,
        "select" => MetadataFieldType.Select,
        "multi_select" => MetadataFieldType.MultiSelect,
        "taxonomy" => MetadataFieldType.Taxonomy,
        "url" => MetadataFieldType.Url,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown metadata field type: {value}")
    };

    public static bool IsValidMetadataSchemaScope(string value) => value is "global" or "asset_type" or "collection";

    public static bool IsValidMetadataFieldType(string value) => value is "text" or "long_text" or "number" or "decimal" or "boolean" or "date" or "date_time" or "select" or "multi_select" or "taxonomy" or "url";

    public static bool IsValidAssetType(string value) => value is "image" or "video" or "document";
}
