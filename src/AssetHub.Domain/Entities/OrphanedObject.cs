namespace AssetHub.Domain.Entities;

/// <summary>
/// Tombstone for a MinIO object that the DB has logically released but
/// storage still holds. Inserted inside the same transaction as the asset /
/// version / brand-logo delete so the DB stays consistent even if the MinIO
/// call fails. A background sweeper drains the table by issuing the actual
/// MinIO DELETE and removing the row on success (A-4 follow-up).
/// </summary>
public class OrphanedObject
{
    public Guid Id { get; set; }

    public string BucketName { get; set; } = string.Empty;

    public string ObjectKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Number of times the sweeper has attempted to delete this object.</summary>
    public int AttemptCount { get; set; }

    /// <summary>UTC timestamp of the most recent sweep attempt, null until first try.</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Truncated error message from the last failed attempt.</summary>
    public string? LastError { get; set; }
}
