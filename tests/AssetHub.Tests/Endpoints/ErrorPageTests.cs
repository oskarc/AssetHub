using System.Globalization;
using System.Net;
using System.Resources;
using System.Text.RegularExpressions;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Ui.Resources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// Guards the status pages: not-found (404), access-denied (403) and the
/// non-Development error page (500), and that machine requests are left alone.
/// </summary>
/// <remarks>
/// <para>
/// Before contract-033 a signed-in user who was denied a page got the sign-in form
/// (AccessDeniedPath pointed at /login), an unknown URL was a blank page, and the
/// production error page was blank too. Nothing in the suite saw any of it: the
/// test scheme answers every challenge with a bare 401 and every forbid with a bare
/// 403, so the cookie handler's redirects never ran.
/// </para>
/// <para>
/// Tests that need the production redirects send
/// <see cref="TestAuthHandler.IdentityChallengeHeader"/>, which hands challenge and
/// forbid to the real Identity cookie handler. Without the header, anonymous and
/// forbidden page requests produce test-host artefacts, so no test here asserts on
/// them. Requests use an https base address: the antiforgery cookie is
/// SecurePolicy.Always outside Development, and rendering a page mints a token.
/// </para>
/// </remarks>
[Collection("Api")]
public class ErrorPageTests : IAsyncLifetime
{
    private const string UnknownPage = "/this-route-does-not-exist";

    private static readonly ResourceManager Common =
        new(typeof(CommonResource).FullName!, typeof(CommonResource).Assembly);

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly CustomWebApplicationFactory _factory;

    public ErrorPageTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        // Rendering a page mints an antiforgery token, which needs the
        // data-protection key ring — a real table, so the schema must exist.
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AssetHubDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Not found ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownPage_WhenSignedIn_Returns404WithNotFoundPage()
    {
        using var client = Client(TestClaimsProvider.Default());

        var response = await client.GetAsync(UnknownPage);
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { Text("Error_PageNotFound") }, Headings(html));
        Assert.Equal($"{Text("Error_PageNotFound")} - {Text("AppName")}", Title(html));
        Assert.Contains("mud-appbar", html); // inside the app shell, not a bare page
    }

    [Fact]
    public async Task UnknownPage_WithSwedishAcceptLanguage_RendersSwedishNotFoundPage()
    {
        using var client = Client(TestClaimsProvider.Default());
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("sv");

        var response = await client.GetAsync(UnknownPage);
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<html lang=\"sv\"", html);
        Assert.Equal(new[] { Text("Error_PageNotFound", "sv") }, Headings(html));
    }

    [Fact]
    public async Task UnknownPage_WhenAnonymous_RedirectsToLogin()
    {
        // No fallback-policy carve-out: an anonymous visitor learns nothing about
        // which routes exist.
        using var client = Client(claims: null, identityChallenge: true);

        var response = await client.GetAsync(UnknownPage);

        AssertRedirect(response, "/login", UnknownPage);
    }

    [Theory]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/_framework/does-not-exist.js")]
    [InlineData("/_blazor/does-not-exist")]
    [InlineData("/_content/does-not-exist.css")]
    [InlineData("/health/does-not-exist")]
    [InlineData("/does-not-exist.png")]
    public async Task ExcludedPath_WhenUnknown_Returns404WithoutBody(string path)
    {
        using var client = Client(TestClaimsProvider.Default());

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownPage_WhenPosted_IsNotReExecuted()
    {
        using var client = Client(TestClaimsProvider.Default());

        var response = await client.PostAsync(UnknownPage, content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    // ── Access denied ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/admin")]
    [InlineData("/all-assets")]
    [InlineData("/admin/users")]
    public async Task RoleRestrictedPage_WhenViewer_RedirectsToAccessDenied(string path)
    {
        using var client = Client(TestClaimsProvider.Default(), identityChallenge: true);

        var response = await client.GetAsync(path);

        AssertRedirect(response, "/access-denied", path);
    }

    [Fact]
    public async Task AccessDeniedPage_WhenSignedIn_Returns403WithAccessDeniedPage()
    {
        using var client = Client(TestClaimsProvider.Default());

        var response = await client.GetAsync("/access-denied?ReturnUrl=%2Fadmin");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { Text("AccessDenied_Title") }, Headings(html));
        Assert.Equal($"{Text("AccessDenied_Title")} - {Text("AppName")}", Title(html));
        Assert.DoesNotContain("id=\"userName\"", html); // not the sign-in form
        Assert.Contains("href=\"/auth/logout\"", html); // "Sign in again"
    }

    [Fact]
    public async Task AccessDeniedPage_WhenAnonymous_RedirectsToLogin()
    {
        using var client = Client(claims: null, identityChallenge: true);

        var response = await client.GetAsync("/access-denied");

        AssertRedirect(response, "/login", "/access-denied");
    }

    // ── Server error (outside Development) ───────────────────────────────────

    [Fact]
    public async Task UnhandledException_OutsideDevelopment_Returns500WithErrorPage()
    {
        using var client = Client(TestClaimsProvider.Default());

        var response = await client.GetAsync(ThrowRouteStartupFilter.Path);
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { Text("Error_Heading") }, Headings(html));
        Assert.Contains(Text("Error_RequestId"), html);
        Assert.DoesNotContain(ThrowRouteStartupFilter.ExceptionMessage, html);
        // The handler runs before the security headers, so the error page keeps them.
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task ErrorPage_OutsideDevelopment_IsStaticWithoutDeveloperGuidance()
    {
        using var client = Client(claims: null);

        var response = await client.GetAsync("/Error");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { Text("Error_Heading") }, Headings(html));
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", html);
        Assert.DoesNotContain("Development", html);
        Assert.DoesNotContain("\"type\":\"server\"", html); // no circuit on the error page
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private HttpClient Client(TestClaimsProvider? claims, bool identityChallenge = false)
    {
        TestAuthHandler.ClaimsOverride = claims;
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        if (identityChallenge)
            client.DefaultRequestHeaders.Add(TestAuthHandler.IdentityChallengeHeader, "1");
        return client;
    }

    private static string Text(string key, string culture = "en") =>
        Common.GetString(key, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException($"CommonResource has no key '{key}'.");

    // Blazor HTML-encodes non-ASCII text (&#xE4;), so compare decoded markup.
    private static async Task<string> HtmlAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private static string[] Headings(string html) =>
        Regex.Matches(html, "<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline, RegexTimeout)
            .Select(m => Regex.Replace(m.Groups[1].Value, "<[^>]*>", string.Empty, RegexOptions.None, RegexTimeout).Trim())
            .ToArray();

    private static string? Title(string html)
    {
        var match = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.Singleline, RegexTimeout);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void AssertRedirect(HttpResponseMessage response, string expectedPath, string returnUrl)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal(expectedPath, location.AbsolutePath);
        Assert.Equal("?ReturnUrl=" + Uri.EscapeDataString(returnUrl), location.Query);
    }
}
