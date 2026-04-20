using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// End-to-end test for the PAT authentication path. Runs against a WebApplicationFactory
/// that keeps the production auth chain (Smart selector + PAT handler + JWT + Cookie),
/// so a request with <c>Authorization: Bearer pat_*</c> exercises every piece of the
/// pipeline that TestAuthHandler normally short-circuits.
/// </summary>
[Collection("PatAuth")]
public class PersonalAccessTokenAuthE2ETests : IAsyncLifetime
{
    private readonly PatAuthWebApplicationFactory _factory;

    public PersonalAccessTokenAuthE2ETests(PatAuthWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        db.PersonalAccessTokens.RemoveRange(db.PersonalAccessTokens);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PatBearer_RoutesThroughSmartSelector_AndAuthenticatesRealRequest()
    {
        var keycloakCalls = new List<string>();
        _factory.MockKeycloak
            .Setup(k => k.GetUserRealmRolesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((userId, _) => keycloakCalls.Add(userId))
            .ReturnsAsync(new HashSet<string> { "viewer" });

        // Mint a real PAT via the service so this test isolates the authentication path
        // rather than the creation endpoint (which is covered in the endpoint test class).
        string plaintext;
        using (var scope = _factory.Services.CreateScope())
        {
            var patService = ActivatorUtilities.CreateInstance<PersonalAccessTokenService>(
                scope.ServiceProvider,
                new CurrentUser("user-e2e-001", isSystemAdmin: false));

            var created = await patService.CreateAsync(
                new CreatePersonalAccessTokenRequest { Name = "e2e", Scopes = [] },
                CancellationToken.None);

            Assert.True(created.IsSuccess);
            plaintext = created.Value!.PlaintextToken;
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

        // /__build only requires an authenticated user (no role policy), so a 200 here
        // proves the full chain: Smart scheme selector routed the bearer to the PAT
        // handler, the handler loaded the token row, stamped sub+pat_id+role claims, and
        // the authenticated principal passed authorisation. This is the `curl`-equivalent
        // scenario documented in the feature spec.
        var response = await client.GetAsync("/__build");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(keycloakCalls); // PAT handler MUST hydrate realm roles via Keycloak
    }

    [Fact]
    public async Task PatBearer_InvalidPlaintext_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "pat_definitely-not-a-real-token");

        var response = await client.GetAsync("/api/v1/me/personal-access-tokens");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatBearer_RevokedToken_Returns401()
    {
        _factory.MockKeycloak
            .Setup(k => k.GetUserRealmRolesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "viewer" });

        string plaintext;
        using (var scope = _factory.Services.CreateScope())
        {
            var patService = ActivatorUtilities.CreateInstance<PersonalAccessTokenService>(
                scope.ServiceProvider,
                new CurrentUser("user-e2e-002", isSystemAdmin: false));

            var created = await patService.CreateAsync(
                new CreatePersonalAccessTokenRequest { Name = "doomed", Scopes = [] },
                CancellationToken.None);
            Assert.True(created.IsSuccess);
            plaintext = created.Value!.PlaintextToken;

            var revoke = await patService.RevokeAsync(created.Value.Token.Id, CancellationToken.None);
            Assert.True(revoke.IsSuccess);
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

        var response = await client.GetAsync("/api/v1/me/personal-access-tokens");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
