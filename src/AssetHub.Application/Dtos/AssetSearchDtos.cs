using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

// ── Request ─────────────────────────────────────────────────────────────

/// <summary>
/// Faceted search over assets. All filter lists use AND across dimensions and OR within a dimension
/// (e.g., AssetTypes = [Image, Video] → image OR video; plus CollectionIds = [A, B] → in A or B).
/// Null / empty lists are treated as "no filter on this dimension".
/// </summary>
public class AssetSearchRequest
{
    /// <summary>Free-text query matched against title, description, tags, and searchable metadata values.</summary>
    [StringLength(500)]
    public string? Text { get; set; }

    /// <summary>Filter by asset type token — "image", "video", "document".</summary>
    [MaxLength(10)]
    public List<string>? AssetTypes { get; set; }

    /// <summary>Filter by lifecycle status — "ready", "processing", "failed".</summary>
    [MaxLength(10)]
    public List<string>? Statuses { get; set; }

    /// <summary>Restrict to these collection IDs. Empty = all collections the caller can see.</summary>
    [MaxLength(200)]
    public List<Guid>? CollectionIds { get; set; }

    /// <summary>Filter by presence of these tags (any match).</summary>
    [MaxLength(50)]
    public List<string>? Tags { get; set; }

    /// <summary>Metadata facet filters — metadata-field id → accepted values (text / taxonomy-term id as string).</summary>
    public Dictionary<Guid, List<string>>? MetadataFilters { get; set; }

    /// <summary>Lower bound on CreatedAt.</summary>
    public DateTime? CreatedAfter { get; set; }

    /// <summary>Upper bound on CreatedAt.</summary>
    public DateTime? CreatedBefore { get; set; }

    /// <summary>Sort key — "relevance" (requires Text), "created_desc", "created_asc", "title_asc", "title_desc".</summary>
    [StringLength(50)]
    public string Sort { get; set; } = "created_desc";

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, 200)]
    public int Take { get; set; } = 50;

    /// <summary>
    /// Which facet dimensions to aggregate for this response. Callers should request only the
    /// facets they display so server-side aggregation cost stays bounded.
    /// Accepted values: "asset_type", "status", "collection", "tag", plus metadata-field ids in
    /// the form "meta:{guid}".
    /// </summary>
    [MaxLength(50)]
    public List<string>? Facets { get; set; }
}

// ── Response ────────────────────────────────────────────────────────────

public class AssetSearchResponse
{
    public required List<AssetResponseDto> Items { get; set; }
    public required int TotalCount { get; set; }

    /// <summary>Facet dimension → buckets. Dimension tokens mirror AssetSearchRequest.Facets.</summary>
    public required Dictionary<string, List<FacetBucket>> Facets { get; set; }
}

public class FacetBucket
{
    /// <summary>Machine value for this bucket — the one a client would pass back in a filter list.</summary>
    public required string Value { get; set; }

    /// <summary>Human-friendly label (taxonomy-term label, collection name, etc.). Falls back to Value.</summary>
    public required string Label { get; set; }

    public required int Count { get; set; }
}
