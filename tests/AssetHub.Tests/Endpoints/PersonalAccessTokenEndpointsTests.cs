using System.Net;
using System.Net.Http.Json;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// Integration tests for <c>/api/v1/me/personal-access-tokens</c>.
/// Covers authorization, list/create/revoke round-trips, and the server-side
/// guard that a PAT-authenticated principal cannot mint or revoke PATs.
/// </summary>
[Collection("Api")]
public class PersonalAccessTokenEndpointsTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private const string BasePath = "/api/v1/me/personal-access-tokens";

    public PersonalAccessTokenEndpointsTests(CustomWebApplicationFactory factory) => _factory = factory;

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

    // ── Listing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Anonymous_Returns401()
    {
        var anon = _factory.CreateAuthenticatedClient(claims: null);
        var response = await anon.GetAsync(BasePath);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_AuthenticatedUser_SeesOnlyOwnTokens()
    {
        // TestAuthHandler.ClaimsOverride is static — swap it between each request so
        // Alice and Bob don't share the last-assigned identity.
        var client = _factory.CreateClient();

        SetUser("user-alice", "alice");
        var aliceCreate = await client.PostAsJsonAsync(BasePath, new CreatePersonalAccessTokenRequest { Name = "alice-1", Scopes = [] });
        aliceCreate.EnsureSuccessStatusCode();

        SetUser("user-bob", "bob");
        var bobCreate = await client.PostAsJsonAsync(BasePath, new CreatePersonalAccessTokenRequest { Name = "bob-1", Scopes = [] });
        bobCreate.EnsureSuccessStatusCode();

        SetUser("user-alice", "alice");
        var aliceList = await client.GetFromJsonAsync<List<PersonalAccessTokenDto>>(BasePath);
        Assert.NotNull(aliceList);
        Assert.Single(aliceList!);
        Assert.Equal("alice-1", aliceList![0].Name);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidBody_Returns201WithLocationAndPlaintextToken()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

        var response = await client.PostAsJsonAsync(BasePath, new CreatePersonalAccessTokenRequest
        {
            Name = "my-ci",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Scopes = ["assets:read"]
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenDto>();
        Assert.NotNull(body);
        Assert.StartsWith(IPersonalAccessTokenService.TokenPrefix, body!.PlaintextToken, StringComparison.Ordinal);
        Assert.Equal("my-ci", body.Token.Name);
        Assert.Contains("assets:read", body.Token.Scopes);
    }

    [Fact]
    public async Task Create_InvalidName_Returns400()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

        var response = await client.PostAsJsonAsync(BasePath, new CreatePersonalAccessTokenRequest
        {
            Name = "", // violates [Required, StringLength(MinimumLength=1)]
            Scopes = []
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithPatBearer_Returns403()
    {
        // Priv-escalation guard: a compromised PAT must NOT be able to mint more PATs.
        var client = _factory.CreateAuthenticatedClient(PatClaimsFor("user-alice"));

        var response = await client.PostAsJsonAsync(BasePath, new CreatePersonalAccessTokenRequest
        {
            Name = "escalation-attempt",
            Scopes = []
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Revoke ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_OwnedToken_Returns204()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());
        var created = await client.PostAsJsonAsync(BasePath, new CreatePersonalAccessTokenRequest { Name = "to-kill", Scopes = [] });
        var body = await created.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenDto>();

        var response = await client.DeleteAsync($"{BasePath}/{body!.Token.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_OtherUsersToken_Returns404()
    {
        var client = _factory.CreateClient();

        SetUser("user-bob", "bob");
        var bobsCreate = await client.PostAsJsonAsync(BasePath, new CreatePersonalAccessTokenRequest { Name = "bobs", Scopes = [] });
        var bobsToken = await bobsCreate.Content.ReadFromJsonAsync<CreatedPersonalAccessTokenDto>();

        SetUser("user-alice", "alice");
        var response = await client.DeleteAsync($"{BasePath}/{bobsToken!.Token.Id}");

        // NOT FOUND (not FORBIDDEN) — don't leak whether a given GUID is a real token on another account.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_WithPatBearer_Returns403()
    {
        var client = _factory.CreateAuthenticatedClient(PatClaimsFor("user-alice"));

        var response = await client.DeleteAsync($"{BasePath}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds claims that mimic a PAT-authenticated principal: same role claims a viewer
    /// would have PLUS a <c>pat_id</c> claim, which is what the endpoints use to detect
    /// PAT-based callers and block self-escalation.
    /// </summary>
    private static TestClaimsProvider PatClaimsFor(string userId)
    {
        var provider = TestClaimsProvider.WithUser(userId, userId);
        provider.Claims.Add(new System.Security.Claims.Claim("pat_id", Guid.NewGuid().ToString()));
        return provider;
    }

    /// <summary>
    /// Overwrites the static <see cref="TestAuthHandler.ClaimsOverride"/> before each
    /// request. Needed whenever a single test exercises multiple identities.
    /// </summary>
    private static void SetUser(string userId, string username) =>
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.WithUser(userId, username);
}
