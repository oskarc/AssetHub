namespace AssetHub.Application.Dtos;

/// <summary>
/// Filter parameters for searching all assets across collections.
/// </summary>
public class AssetSearchFilter
{
    public string? Query { get; init; }
    public string? AssetType { get; init; }
    public string SortBy { get; init; } = Constants.SortBy.CreatedDesc;
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
    public List<Guid>? AllowedCollectionIds { get; init; }
    public bool IncludeAllStatuses { get; init; }
}
