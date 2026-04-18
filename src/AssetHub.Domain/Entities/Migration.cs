namespace AssetHub.Domain.Entities;

public class Migration
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MigrationSourceType SourceType { get; set; }
    public MigrationStatus Status { get; set; } = MigrationStatus.Draft;
    public Guid? DefaultCollectionId { get; set; }
    public Dictionary<string, object> SourceConfig { get; set; } = new();
    public Dictionary<string, string> FieldMapping { get; set; } = new();
    public bool DryRun { get; set; }
    public int ItemsTotal { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
    public int ItemsSkipped { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public ICollection<MigrationItem> Items { get; set; } = new List<MigrationItem>();
}
