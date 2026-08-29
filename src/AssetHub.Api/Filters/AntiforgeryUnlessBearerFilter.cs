using Microsoft.AspNetCore.Antiforgery;

namespace AssetHub.Api.Filters;

/// <summary>
/// Endpoint filter that requires a valid antiforgery token for cookie-
/// authenticated requests, and skips entirely for JWT bearer auth
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
/// accept anonymous mutations (share password submit
/// accept) protect themselves with rate limits + signed
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

        // Only unsafe methods can be CSRF'd, and only they carry a token. The
        // built-in AntiforgeryMiddleware makes the same check; IAntiforgery
        // .ValidateRequestAsync does NOT — it validates whatever it is handed.
        // Omitting this 400s every authenticated GET on a gated group, which is
        // every thumbnail, preview and download in the app. It stayed invisible
        // while the scheme-name comparison below kept this branch dead.
        if (HttpMethods.IsGet(http.Request.Method)
            || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method)
            || HttpMethods.IsTrace(http.Request.Method))
        {
            return await next(context);
        }

        // Bearer auth (JWT) is CSRF-immune — skip.
        var authHeader = http.Request.Headers.Authorization;
        if (authHeader.Count > 0
            && authHeader[0] is { } first
            && first.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return await next(context);
        }

        // Anonymous requests carry no credential for a cross-site page to abuse —
        // the public share endpoints depend on this.
        if (http.User.Identity?.IsAuthenticated != true) return await next(context);

        // CSRF is only possible when the credential is AMBIENT — attached by the
        // browser automatically. That means cookies. A request bearing no cookies
        // cannot have been authenticated by one, so there is nothing to forge.
        //
        // Deliberately NOT a scheme-NAME comparison. This filter used to check
        // AuthenticationType against CookieAuthenticationDefaults.AuthenticationScheme
        // ("Cookies"); when contract-015 swapped Keycloak/OIDC for ASP.NET Core
        // Identity the principal's scheme became "Identity.Application", the strings
        // stopped matching, and the gate silently validated NOTHING for every
        // signed-in user — with no test to notice. Keying on the presence of a
        // cookie describes the actual threat and cannot drift when the provider is
        // renamed or replaced again.
        if (http.Request.Cookies.Count == 0) return await next(context);

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
