namespace AssetHub.Domain.Entities;

/// <summary>
/// Scope of a share link.
/// </summary>
public enum ShareScopeType
{
    Asset,
    Collection
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the share enums.
/// </summary>
public static class ShareEnumExtensions
{
    public static string ToDbString(this ShareScopeType scope) => scope switch
    {
        ShareScopeType.Asset => "asset",
        ShareScopeType.Collection => "collection",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    public static ShareScopeType ToShareScopeType(this string value) => value switch
    {
        "asset" => ShareScopeType.Asset,
        "collection" => ShareScopeType.Collection,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown share scope type: {value}")
    };
}
