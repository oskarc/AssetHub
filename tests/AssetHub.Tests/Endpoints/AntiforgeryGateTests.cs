using System.Net;
using System.Net.Http.Json;
using AssetHub.Application.Dtos;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// Guards the CSRF gate (P-12 / A-7).
/// </summary>
/// <remarks>
/// <para>
/// This gate was INERT for four contracts and nothing noticed. The filter decided
/// whether to validate by comparing the principal's <c>AuthenticationType</c> to
/// <c>CookieAuthenticationDefaults.AuthenticationScheme</c> — the literal
/// <c>"Cookies"</c>. When contract-015 replaced Keycloak/OIDC with ASP.NET Core
/// Identity, principals began authenticating as <c>"Identity.Application"</c>, the
/// comparison stopped matching, and every signed-in request skipped validation.
/// </para>
/// <para>
/// It went unseen because the suite had only positive tests: nothing asserted that
/// a mutation WITHOUT a token is refused, so a gate that refused nothing looked
/// exactly like a gate that worked. The negative case below is the one that matters.
/// </para>
/// </remarks>
[Collection("Api")]
public class AntiforgeryGateTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AntiforgeryGateTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AssetHubDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Mutation_WithAmbientCookieAndNoToken_IsRejected()
    {
        using var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());

        // The antiforgery cookie is SecurePolicy.Always outside Development, and the
        // antiforgery system throws if the request is not SSL. Production is HTTPS;
        // the test host defaults to HTTP, so address it as HTTPS or the SSL check
        // fires before token validation is ever reached.
        client.BaseAddress = new Uri("https://localhost");

        // A cookie makes the credential ambient — exactly the browser-borne shape
        // CSRF exploits. Without an X-CSRF-TOKEN this must not be honoured.
        // Targets a SURVIVING mutation. contract-023 cut the uncalled REST surface,
        // leaving only the two download-all POSTs — so this gate is now guarding
        // exactly the endpoints a browser can still reach. The collection id need not
        // exist: the antiforgery filter runs before the handler.
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/collections/{Guid.NewGuid()}/download-all");
        request.Headers.Add("Cookie", "assethub.auth=forged-ambient-credential");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"A cookie-bearing mutation with no antiforgery token must be refused, got {(int)response.StatusCode}: {body} "
            + "If this passes as success the CSRF gate is inert again — check that the filter has not gone "
            + "back to comparing AuthenticationType against a scheme name.");
    }

    [Fact]
    public async Task Mutation_WithNoAmbientCredential_IsAllowed()
    {
        // No cookies: the credential cannot have been attached automatically by a
        // browser, so there is nothing to forge and the gate must not interfere.
        using var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());

        var response = await client.PostAsync(
            $"/api/v1/collections/{Guid.NewGuid()}/download-all", content: null);

        // The property under test is that the GATE does not interfere — not that the
        // request succeeds. A missing collection legitimately answers 404; only a 400
        // would mean antiforgery rejected a request carrying no ambient credential.
        Assert.True(
            response.StatusCode != HttpStatusCode.BadRequest,
            $"The gate must not challenge a request with no ambient credential, got {(int)response.StatusCode}.");
    }
    /// <summary>
    /// A safe method must never be antiforgery-validated.
    /// </summary>
    /// <remarks>
    /// Regression guard. <c>IAntiforgery.ValidateRequestAsync</c> validates whatever
    /// it is handed — unlike the built-in middleware, it does not check the HTTP
    /// method. When the gate's dead scheme-name branch was made live, every
    /// authenticated GET on a gated group started returning 400: thumbnails,
    /// previews and downloads, i.e. most of what the UI fetches over HTTP.
    ///
    /// The original two gate tests both used POST, so they could not see it.
    /// </remarks>
    [Fact]
    public async Task SafeMethod_WithAmbientCookieAndNoToken_IsNotValidated()
    {
        using var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());

        var response = await client.GetAsync($"/api/v1/zip-downloads/{Guid.NewGuid()}");

        Assert.False(
            response.StatusCode == HttpStatusCode.BadRequest,
            "An authenticated GET must not be antiforgery-validated — only unsafe "
            + "methods carry a token, so validating a GET breaks every media fetch in the UI.");
    }

}
