namespace AssetHub.Application.Dtos;

public class TrashListResponse
{
    public required List<TrashedAssetDto> Items { get; set; }
    public required int TotalCount { get; set; }
}

public class TrashedAssetDto
{
    public required Guid Id { get; set; }
    public required string Title { get; set; }
    public required string AssetType { get; set; }
    public required long SizeBytes { get; set; }
    public string? ThumbObjectKey { get; set; }
    public string? PosterObjectKey { get; set; }
    public required DateTime DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }

    /// <summary>
    /// When the asset will be permanently purged by the background worker, based on
    /// AssetLifecycleSettings.TrashRetentionDays from the server's point of view.
    /// </summary>
    public required DateTime ExpiresAt { get; set; }
}

public class EmptyTrashResponse
{
    public required int Purged { get; set; }
    public required int Failed { get; set; }
}
