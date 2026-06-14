using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssetHub.Infrastructure.Data.Configurations;

/// <summary>
/// Shared column conventions and JSONB value comparers/converters used across the
/// per-entity <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/>
/// classes. Lifted out of <c>AssetHubDbContext</c> when the inline config blocks were
/// split into per-entity classes — these must stay identical to preserve the model.
/// </summary>
internal static class ModelConventions
{
    public const string Jsonb = "jsonb";
    public const string TextArray = "text[]";

    public static readonly ValueComparer<List<string>> StringListComparer = new(
        (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        c => c.ToList());

    public static readonly ValueComparer<Dictionary<string, object>> JsonbDictionaryComparer = new(
        (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
        c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
        c => JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!);

    public static readonly ValueConverter<Dictionary<string, object>, string> JsonbDictionaryConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>());
}
