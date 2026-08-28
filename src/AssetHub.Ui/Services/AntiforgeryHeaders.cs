using Microsoft.AspNetCore.Components.Forms;

namespace AssetHub.Ui.Services;

/// <summary>
/// Builds the antiforgery header for browser-side <c>fetch</c> calls that mutate.
/// </summary>
/// <remarks>
/// <para>
/// Most UI mutations go through the in-process facade and never touch HTTP, but a
/// few flows POST from the browser — the ZIP "Download all" paths, which hand a URL
/// to <c>enqueueAndPollZipDownload</c>. Those requests carry the auth cookie, so
/// <see cref="AssetHub.Api.Filters.AntiforgeryUnlessBearerFilter"/> validates them
/// and they need a token.
/// </para>
/// <para>
/// The token cannot come from <c>IAntiforgery</c> here: that needs an
/// <c>HttpContext</c>, which no longer exists once the circuit is interactive.
/// <see cref="AntiforgeryStateProvider"/> is the supported route — it carries the
/// token across the prerender boundary into component state.
/// </para>
/// </remarks>
public static class AntiforgeryHeaders
{
    /// <summary>Matches <c>AddAntiforgery(options =&gt; options.HeaderName)</c> in the API host.</summary>
    public const string HeaderName = "X-CSRF-TOKEN";

    /// <summary>
    /// Returns <paramref name="existing"/> plus the antiforgery header. Returns the
    /// headers unchanged when no token is available (an anonymous visitor on a public
    /// share page) — the filter skips validation for unauthenticated requests, so
    /// those flows keep working.
    /// </summary>
    public static Dictionary<string, string> Build(
        AntiforgeryStateProvider antiforgery,
        IDictionary<string, string>? existing = null)
    {
        var headers = existing is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(existing);

        if (antiforgery.GetAntiforgeryToken() is { } token)
            headers[HeaderName] = token.Value;

        return headers;
    }
}
