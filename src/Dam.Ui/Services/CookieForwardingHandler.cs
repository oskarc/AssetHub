using Microsoft.AspNetCore.Http;

namespace Dam.Ui.Services;

/// <summary>
/// Forwards authentication cookies from the current HTTP context to outgoing API requests.
/// This enables Blazor Server components to call the same-origin API with the user's session.
/// </summary>
public class CookieForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CookieForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            // Forward all cookies from the incoming request
            var cookieHeader = httpContext.Request.Headers.Cookie.ToString();
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.Add("Cookie", cookieHeader);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
