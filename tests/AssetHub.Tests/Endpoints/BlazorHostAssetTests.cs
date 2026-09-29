using System.Net;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// Regression guard for the Blazor client script.
/// </summary>
/// <remarks>
/// <para>
/// <c>blazor.web.js</c> ships in the NuGet package
/// <c>Microsoft.AspNetCore.App.Internal.Assets</c>, which the SDK adds
/// <b>implicitly, and only when a Web project contains Razor content of its
/// own</b>. While <c>App.razor</c> lived in the <c>AssetHub.Ui</c> RCL,
/// <c>AssetHub.Api</c> contained zero <c>.razor</c> files, so the SDK never
/// classified the host as a Blazor app and never published its client assets.
/// </para>
/// <para>
/// The failure mode is what makes this test worth its cost: the build was
/// green, the whole unit suite passed, the server rendered correct HTML and the
/// <c>/_blazor</c> hub answered — while no user could click anything, because
/// the script that starts the circuit 404'd. Nothing in the suite could see it.
/// </para>
/// <para>
/// So this asserts the one fact no other test covers: the host still serves its
/// own framework script. If <c>App.razor</c> is ever moved back into a library,
/// or the host's Razor content is otherwise removed, this fails.
/// </para>
/// </remarks>
[Collection("Api")]
public class BlazorHostAssetTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public BlazorHostAssetTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        // Rendering a page mints an antiforgery token, which needs the
        // data-protection key ring — a real table, so the schema must exist.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FrameworkScript_IsServedByTheHost_WithExecutableContentType()
    {
        // Anonymous on purpose: the script must load before sign-in, otherwise
        // the login page itself has no circuit. ClaimsOverride is static and
        // shared across the "Api" collection, so anonymity is set, not assumed.
        TestAuthHandler.ClaimsOverride = null;
        using var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/_framework/blazor.web.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.True(
            contentType is "text/javascript" or "application/javascript",
            $"Expected an executable content type for blazor.web.js but got '{contentType}'. "
            + "A non-JS content type means the browser will refuse to run it.");

        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    /// <summary>
    /// Guards server-side routable-component discovery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MapRazorComponents&lt;App&gt;()</c> discovers routable pages from
    /// <c>App</c>'s own assembly. <c>App</c> lives in <c>AssetHub.Api</c> while
    /// every page lives in the <c>AssetHub.Ui</c> RCL, so that assembly has to be
    /// named through <c>AddAdditionalAssemblies</c>. This is a different knob from
    /// <c>Router.AdditionalAssemblies</c> in <c>Routes.razor</c>, which only drives
    /// interactive client-side routing.
    /// </para>
    /// <para>
    /// Drop it and no page endpoint carries its own
    /// <c>[Authorize]</c>/<c>[AllowAnonymous]</c> metadata, so every route falls to
    /// the fallback policy — turning <c>/login</c> into a redirect to itself and
    /// locking out anyone not already signed in. The build stays clean and the rest
    /// of the suite stays green, because <c>TestAuthHandler</c> authenticates every
    /// other test's requests and so cannot see it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnonymousPage_RendersWithoutSignIn()
    {
        // Set, not assumed: a signed-in identity left by an earlier test would pass
        // the fallback policy and hide exactly the failure this guards.
        TestAuthHandler.ClaimsOverride = null;
        using var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/login");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"/login is [AllowAnonymous] but answered {(int)response.StatusCode} "
            + $"(Location: {response.Headers.Location?.ToString() ?? "none"}). A redirect here means page "
            + "endpoints lost their per-page auth metadata — check AddAdditionalAssemblies "
            + "on MapRazorComponents<App>().");
    }
}
