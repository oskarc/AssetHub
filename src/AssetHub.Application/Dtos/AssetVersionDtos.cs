namespace AssetHub.Application.Dtos;

public class AssetVersionDto
{
    public required Guid Id { get; set; }
    public required Guid AssetId { get; set; }
    public required int VersionNumber { get; set; }
    public string? ThumbObjectKey { get; set; }
    public required long SizeBytes { get; set; }
    public required string ContentType { get; set; }
    public required string Sha256 { get; set; }
    public required string CreatedByUserId { get; set; }
    public required DateTime CreatedAt { get; set; }
    public string? ChangeNote { get; set; }

    /// <summary>True when this version matches the asset's CurrentVersionNumber.</summary>
    public required bool IsCurrent { get; set; }
}
