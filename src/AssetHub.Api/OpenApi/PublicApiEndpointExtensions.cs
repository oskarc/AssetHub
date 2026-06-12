using AssetHub.Api.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AssetHub.Api.OpenApi;

/// <summary>
/// Endpoint convention helpers for the public OpenAPI surface.
///
/// Prefer <see cref="MarkAsPublicRead{TBuilder}"/> / <see cref="MarkAsPublicMutation{TBuilder}"/>
/// over hand-chaining the individual calls: they encode the house security bundle
/// (PAT scope enforcement + antiforgery handling + OpenAPI inclusion) as one call,
/// so a public endpoint cannot ship missing one leg of it. The P-12 / A-7
/// regressions both came from hand-assembled chains dropping a leg.
/// </summary>
public static class PublicApiEndpointExtensions
{
    /// <summary>
    /// Marks the endpoint (or every endpoint in the group) as part of the stable
    /// public API that appears in the generated OpenAPI document.
    /// Prefer the composed helpers below for per-endpoint use; call this directly
    /// only on groups or on the documented scope-filter exceptions (PAT self-service).
    /// </summary>
    public static TBuilder MarkAsPublicApi<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new PublicApiAttribute());
        return builder;
    }

    /// <summary>
    /// Public-API <b>read</b> endpoint: applies the PAT scope filter and includes the
    /// endpoint in the OpenAPI document. GETs are exempt from antiforgery by default,
    /// so no antiforgery leg is needed.
    /// </summary>
    public static TBuilder MarkAsPublicRead<TBuilder>(this TBuilder builder, RequireScopeFilter scope)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(scope).MarkAsPublicApi();

    /// <summary>
    /// Public-API <b>mutating</b> endpoint (POST/PATCH/PUT/DELETE): applies the PAT scope
    /// filter, disables the default ASP.NET antiforgery pipeline (the group-level
    /// <c>RequireAntiforgeryUnlessBearer()</c> remains the actual CSRF gate for cookie
    /// principals — both halves are required, see CLAUDE.md § Route groups), and includes
    /// the endpoint in the OpenAPI document.
    /// Chain any <c>ValidationFilter&lt;T&gt;</c> <b>before</b> this call so validation
    /// keeps running ahead of the scope check.
    /// </summary>
    public static TBuilder MarkAsPublicMutation<TBuilder>(this TBuilder builder, RequireScopeFilter scope)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter(scope).DisableAntiforgery().MarkAsPublicApi();
}
