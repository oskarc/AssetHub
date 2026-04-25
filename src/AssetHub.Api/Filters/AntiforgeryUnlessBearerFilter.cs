using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AssetHub.Api.Filters;

/// <summary>
/// Endpoint filter that requires a valid antiforgery token for cookie-
/// authenticated requests, and skips entirely for JWT / PAT bearer auth
/// (which is CSRF-immune by construction — the token must be explicitly
/// attached, browsers never auto-send it). Closes P-12 / A-7 in the
/// security review: an XSS in the Blazor UI used to be able to call
/// any mutating <c>/api/v1/*</c> endpoint with the user's cookie; now
/// the request also has to present the matching antiforgery header.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous requests (no authenticated principal) skip the check —
/// they can't have an antiforgery session yet, and the endpoints that
/// accept anonymous mutations (share password submit, guest invitation
/// accept, unsubscribe) protect themselves with rate limits + signed
/// tokens instead.
/// </para>
/// <para>
/// On a missing / mismatched header the filter returns <c>400</c> with a
/// short body — never <c>403</c>, since that's reserved for authorization
/// failures and antiforgery isn't an authorization concept.
/// </para>
/// </remarks>
public sealed class AntiforgeryUnlessBearerFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // Bearer auth (JWT or PAT) is CSRF-immune — skip.
        var authHeader = http.Request.Headers.Authorization;
        if (authHeader.Count > 0
            && authHeader[0] is { } first
            && first.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return await next(context);
        }

        // Only validate when the resolved principal is cookie-authenticated.
        // PAT and JWT principals are caught by the Bearer check above; this
        // catches the case where a request has no Authorization header AND
        // no cookie session either (anonymous fall-through).
        var isCookieAuth =
            http.User.Identity?.IsAuthenticated == true
            && string.Equals(
                http.User.Identity.AuthenticationType,
                CookieAuthenticationDefaults.AuthenticationScheme,
                StringComparison.Ordinal);
        if (!isCookieAuth) return await next(context);

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException ex)
        {
            return Results.Problem(
                title: "Antiforgery validation failed.",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        return await next(context);
    }
}
