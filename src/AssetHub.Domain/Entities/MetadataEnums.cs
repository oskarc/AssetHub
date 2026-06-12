namespace AssetHub.Domain.Entities;

/// <summary>
/// Scope of a metadata schema — determines which assets it applies to.
/// </summary>
public enum MetadataSchemaScope
{
    Global,
    AssetType,
    Collection
}

/// <summary>
/// Data type for a metadata field.
/// </summary>
public enum MetadataFieldType
{
    Text,
    LongText,
    Number,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Select,
    MultiSelect,
    Taxonomy,
    Url
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the metadata schema enums.
/// </summary>
// Each enum's ToDbString is kept next to its parser instead of grouping all
// ToDbString overloads together — easier to scan per-enum. Suppress S4136 for the file.
#pragma warning disable S4136
public static class MetadataEnumExtensions
{
    private const string Collection = "collection";

    public static string ToDbString(this MetadataSchemaScope scope) => scope switch
    {
        MetadataSchemaScope.Global => "global",
        MetadataSchemaScope.AssetType => "asset_type",
        MetadataSchemaScope.Collection => Collection,
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    public static MetadataSchemaScope ToMetadataSchemaScope(this string value) => value switch
    {
        "global" => MetadataSchemaScope.Global,
        "asset_type" => MetadataSchemaScope.AssetType,
        Collection => MetadataSchemaScope.Collection,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown metadata schema scope: {value}")
    };

    private static readonly HashSet<string> ValidMetadataSchemaScopes =
        new(StringComparer.Ordinal) { "global", "asset_type", Collection };

    public static bool IsValidMetadataSchemaScope(string value) => ValidMetadataSchemaScopes.Contains(value);

    public static string ToDbString(this MetadataFieldType type) => type switch
    {
        MetadataFieldType.Text => "text",
        MetadataFieldType.LongText => "long_text",
        MetadataFieldType.Number => "number",
        MetadataFieldType.Decimal => "decimal",
        MetadataFieldType.Boolean => "boolean",
        MetadataFieldType.Date => "date",
        MetadataFieldType.DateTime => "date_time",
        MetadataFieldType.Select => "select",
        MetadataFieldType.MultiSelect => "multi_select",
        MetadataFieldType.Taxonomy => "taxonomy",
        MetadataFieldType.Url => "url",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static MetadataFieldType ToMetadataFieldType(this string value) => value switch
    {
        "text" => MetadataFieldType.Text,
        "long_text" => MetadataFieldType.LongText,
        "number" => MetadataFieldType.Number,
        "decimal" => MetadataFieldType.Decimal,
        "boolean" => MetadataFieldType.Boolean,
        "date" => MetadataFieldType.Date,
        "date_time" => MetadataFieldType.DateTime,
        "select" => MetadataFieldType.Select,
        "multi_select" => MetadataFieldType.MultiSelect,
        "taxonomy" => MetadataFieldType.Taxonomy,
        "url" => MetadataFieldType.Url,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown metadata field type: {value}")
    };

    private static readonly HashSet<string> ValidMetadataFieldTypes = new(StringComparer.Ordinal)
    {
        "text", "long_text", "number", "decimal", "boolean",
        "date", "date_time", "select", "multi_select", "taxonomy", "url"
    };

    public static bool IsValidMetadataFieldType(string value) => ValidMetadataFieldTypes.Contains(value);
}
#pragma warning restore S4136
