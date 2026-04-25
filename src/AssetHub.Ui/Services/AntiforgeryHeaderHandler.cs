using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace AssetHub.Ui.Services;

/// <summary>
/// Attaches the antiforgery <c>X-CSRF-TOKEN</c> header on outbound mutating
/// requests so the server-side <c>AntiforgeryUnlessBearerFilter</c> on
/// <c>/api/v1/*</c> accepts them. Pairs with <see cref="CookieForwardingHandler"/>:
/// the cookie carries one half of the antiforgery pair, this handler
/// supplies the matching header (P-12 / A-7).
/// </summary>
/// <remarks>
/// <para>
/// Skipped for safe methods (GET / HEAD / OPTIONS / TRACE) and for
/// requests that travel without an HttpContext (e.g., background tasks
/// in the same process). The token comes from
/// <see cref="IAntiforgery.GetAndStoreTokens"/> against the user's
/// current request — the same call also writes the antiforgery cookie
/// to the response if it isn't there yet, ensuring the client browser
/// has it for subsequent requests.
/// </para>
/// </remarks>
public sealed class AntiforgeryHeaderHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAntiforgery _antiforgery;

    public AntiforgeryHeaderHandler(IHttpContextAccessor httpContextAccessor, IAntiforgery antiforgery)
    {
        _httpContextAccessor = httpContextAccessor;
        _antiforgery = antiforgery;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsSafeMethod(request.Method))
            return await base.SendAsync(request, cancellationToken);

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
            return await base.SendAsync(request, cancellationToken);

        // Reading + storing the tokens here also persists the cookie on the
        // response if missing. Subsequent calls in the same circuit reuse it.
        var tokens = _antiforgery.GetAndStoreTokens(httpContext);
        if (!string.IsNullOrEmpty(tokens.RequestToken))
        {
            request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", tokens.RequestToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsSafeMethod(HttpMethod method)
        => HttpMethods.IsGet(method.Method)
        || HttpMethods.IsHead(method.Method)
        || HttpMethods.IsOptions(method.Method)
        || HttpMethods.IsTrace(method.Method);
}
