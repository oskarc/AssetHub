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
    /// Source type — currently only "csv_upload".
    /// </summary>
    [Required]
    [RegularExpression("^(csv_upload)$", ErrorMessage = "Source type must be 'csv_upload'.")]
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
