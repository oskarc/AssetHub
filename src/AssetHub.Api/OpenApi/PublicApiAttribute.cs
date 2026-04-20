namespace AssetHub.Api.OpenApi;

/// <summary>
/// Marks an endpoint (or endpoint group) as part of the stable, documented public
/// REST API. Only endpoints carrying this attribute are included in the generated
/// OpenAPI document at <c>/swagger/v1/swagger.json</c>. Removing or renaming a
/// public endpoint is a breaking change and must be versioned accordingly.
/// Admin/internal endpoints omit the attribute so they stay out of the public
/// schema even though they remain functionally available to authorised callers.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PublicApiAttribute : Attribute
{
}
