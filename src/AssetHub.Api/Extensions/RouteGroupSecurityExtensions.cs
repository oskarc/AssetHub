using AssetHub.Api.Filters;

namespace AssetHub.Api.Extensions;

/// <summary>
/// Route-group extensions for security filters that apply uniformly to a
/// group of endpoints — saves repeating <c>.AddEndpointFilter</c> on every
/// individual <c>MapPost</c> / <c>MapPatch</c> / <c>MapDelete</c>.
/// </summary>
public static class RouteGroupSecurityExtensions
{
    /// <summary>
    /// Applies <see cref="AntiforgeryUnlessBearerFilter"/> to every endpoint
    /// in the group. CSRF protection kicks in for cookie-authenticated
    /// requests; Bearer (JWT / PAT) requests pass through unchanged.
    /// </summary>
    public static RouteGroupBuilder RequireAntiforgeryUnlessBearer(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<AntiforgeryUnlessBearerFilter>();
        return group;
    }

    /// <summary>
    /// Per-endpoint variant of the above. Used where a single mapped
    /// endpoint re-applies <c>RequireAuthorization</c> outside any group
    /// (e.g., admin-policy escalation on a bulk endpoint).
    /// </summary>
    public static RouteHandlerBuilder RequireAntiforgeryUnlessBearer(this RouteHandlerBuilder builder)
    {
        builder.AddEndpointFilter<AntiforgeryUnlessBearerFilter>();
        return builder;
    }
}
