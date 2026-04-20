using Microsoft.AspNetCore.Builder;

namespace AssetHub.Api.OpenApi;

/// <summary>
/// Endpoint convention helpers for the public OpenAPI surface.
/// </summary>
public static class PublicApiEndpointExtensions
{
    /// <summary>
    /// Marks the endpoint (or every endpoint in the group) as part of the stable
    /// public API that appears in the generated OpenAPI document.
    /// </summary>
    public static TBuilder MarkAsPublicApi<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new PublicApiAttribute());
        return builder;
    }
}
