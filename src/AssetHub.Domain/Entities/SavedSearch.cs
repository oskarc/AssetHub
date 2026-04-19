namespace AssetHub.Domain.Entities;

public class SavedSearch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>
    /// Serialized AssetSearchRequest — replayed when re-running the saved search.
    /// </summary>
    public string RequestJson { get; set; } = string.Empty;

    public SavedSearchNotifyCadence Notify { get; set; } = SavedSearchNotifyCadence.None;

    /// <summary>
    /// Populated by the notification worker when it runs the saved search.
    /// Schema-only in v1; the worker ships with T3-NTF-01.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Highest asset id seen in the last notification run — used to detect newly-matching assets
    /// without re-reading the whole result set. Schema-only in v1.
    /// </summary>
    public Guid? LastHighestSeenAssetId { get; set; }

    public DateTime CreatedAt { get; set; }
}
