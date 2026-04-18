namespace AssetHub.Domain.Entities;

public class MigrationItem
{
    public Guid Id { get; set; }
    public Guid MigrationId { get; set; }
    public Migration? Migration { get; set; }
    public MigrationItemStatus Status { get; set; } = MigrationItemStatus.Pending;
    public string? ExternalId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
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
