using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace AssetHub.Api.OpenApi;

/// <summary>
/// Replaces schemas generated from <see cref="Dictionary{TKey, TValue}"/> where the value
/// type is <see cref="object"/> with an open-ended object schema (<c>additionalProperties: true</c>).
/// The default .NET 9 schema generator throws <c>"The node must be of type 'JsonValue'"</c> when
/// it encounters these dictionaries because it cannot synthesise a schema for the <c>object</c>
/// value type. The affected fields (e.g. <c>MetadataJson</c>) hold arbitrary JSON anyway, so an
/// open-ended object is the correct shape to document.
/// </summary>
internal sealed class DictionaryOfObjectSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (!IsDictionaryOfObject(type))
            return Task.CompletedTask;

        schema.Type = "object";
        schema.Properties = new Dictionary<string, OpenApiSchema>();
        schema.AdditionalPropertiesAllowed = true;
        schema.AdditionalProperties = new OpenApiSchema();
        schema.Example = new OpenApiObject();
        return Task.CompletedTask;
    }

    private static bool IsDictionaryOfObject(Type type)
    {
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        if (def != typeof(Dictionary<,>) && def != typeof(IDictionary<,>) && def != typeof(IReadOnlyDictionary<,>))
            return false;
        var args = type.GetGenericArguments();
        return args.Length == 2 && args[1] == typeof(object);
    }
}
