namespace AssetHub.Application.Dtos;

public class AssetResponseDto
{
    public required Guid Id { get; set; }
    public required string AssetType { get; set; }
    public required string Status { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Copyright { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> MetadataJson { get; set; } = new();
    public required string ContentType { get; set; }
    public required long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? ThumbObjectKey { get; set; }
    public string? MediumObjectKey { get; set; }
    public string? PosterObjectKey { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public required DateTime UpdatedAt { get; set; }

    /// <summary>The ID of the source asset this was derived from (via image editing).</summary>
    public Guid? SourceAssetId { get; set; }

    /// <summary>True when this asset has a stored edit document that can be re-opened.</summary>
    public bool HasEditDocument { get; set; }

    /// <summary>The stored edit document JSON (only populated in single-asset responses).</summary>
    public string? EditDocument { get; set; }

    /// <summary>Number of derivative assets created from this asset.</summary>
    public int DerivativeCount { get; set; }

    /// <summary>
    /// The current user's role in this asset's collection.
    /// Used for UI visibility of actions. Values: "viewer", "contributor", "manager", "admin"
    /// </summary>
    public string UserRole { get; set; } = "viewer";
}
