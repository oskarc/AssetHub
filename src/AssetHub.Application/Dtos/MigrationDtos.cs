using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// DTO for creating a new migration job.
/// </summary>
public class CreateMigrationDto
{
    /// <summary>
    /// Human-readable name for this migration batch.
    /// </summary>
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Name { get; set; }

    /// <summary>
    /// Source type — one of "csv_upload" or "s3".
    /// </summary>
    [Required]
    [RegularExpression("^(csv_upload|s3)$", ErrorMessage = "Source type must be 'csv_upload' or 's3'.")]
    public required string SourceType { get; set; }

    /// <summary>
    /// Optional default collection ID to assign imported assets to.
    /// Mutually exclusive with DefaultCollectionName.
    /// </summary>
    public Guid? DefaultCollectionId { get; set; }

    /// <summary>
    /// Optional name for a new collection to create and assign imported assets to.
    /// Mutually exclusive with DefaultCollectionId.
    /// </summary>
    [StringLength(255)]
    public string? DefaultCollectionName { get; set; }

    /// <summary>
    /// Whether this is a dry run (validate only, no actual imports).
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// S3 connector configuration — required when <see cref="SourceType"/> is "s3", ignored otherwise.
    /// </summary>
    public S3SourceConfigDto? S3Config { get; set; }
}

/// <summary>
/// S3 / MinIO pull connector configuration. The secret key is encrypted at rest via
/// ASP.NET Core Data Protection before being persisted into <c>Migration.SourceConfig</c>.
/// </summary>
public class S3SourceConfigDto
{
    /// <summary>
    /// Full endpoint URL, e.g. "https://s3.eu-west-1.amazonaws.com" or "http://minio.local:9000".
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    [Url]
    public required string Endpoint { get; set; }

    /// <summary>
    /// Bucket name.
    /// </summary>
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string Bucket { get; set; }

    /// <summary>
    /// Optional object-key prefix to restrict scanning.
    /// </summary>
    [StringLength(1024)]
    public string? Prefix { get; set; }

    /// <summary>
    /// Access key identifier.
    /// </summary>
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public required string AccessKey { get; set; }

    /// <summary>
    /// Secret access key. Encrypted before persistence; never stored or logged in plaintext.
    /// </summary>
    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public required string SecretKey { get; set; }

    /// <summary>
    /// Optional AWS region (e.g. "eu-west-1"). Not required for MinIO / non-AWS endpoints.
    /// </summary>
    [StringLength(64)]
    public string? Region { get; set; }
}

/// <summary>
/// Response DTO for a migration job.
/// </summary>
public class MigrationResponseDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required string SourceType { get; set; }
    public required string Status { get; set; }
    public Guid? DefaultCollectionId { get; set; }
    public bool DryRun { get; set; }
    public int ItemsTotal { get; set; }
    public int ItemsStaged { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
    public int ItemsSkipped { get; set; }
    public required string CreatedByUserId { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// Response DTO for a single migration item.
/// </summary>
public class MigrationItemResponseDto
{
    public required Guid Id { get; set; }
    public required Guid MigrationId { get; set; }
    public required string Status { get; set; }
    public string? ExternalId { get; set; }
    public required string FileName { get; set; }
    public string? SourcePath { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Copyright { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> CollectionNames { get; set; } = new();
    public Dictionary<string, object> MetadataJson { get; set; } = new();
    public string? Sha256 { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public bool IsFileStaged { get; set; }
    public Guid? AssetId { get; set; }
    public int RowNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

/// <summary>
/// Paginated response for migration items.
/// </summary>
public class MigrationItemListResponse
{
    public required List<MigrationItemResponseDto> Items { get; set; }
    public required int TotalCount { get; set; }
}

/// <summary>
/// Paginated response for migrations.
/// </summary>
public class MigrationListResponse
{
    public required List<MigrationResponseDto> Migrations { get; set; }
    public required int TotalCount { get; set; }
}

/// <summary>
/// Summary of migration progress.
/// </summary>
public class MigrationProgressDto
{
    public required Guid Id { get; set; }
    public required string Status { get; set; }
    public int ItemsTotal { get; set; }
    public int ItemsStaged { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
    public int ItemsSkipped { get; set; }
    public int ItemsProcessed => ItemsSucceeded + ItemsFailed + ItemsSkipped;
    public double ProgressPercent => ItemsTotal > 0 ? (double)ItemsProcessed / ItemsTotal * 100 : 0;
}
