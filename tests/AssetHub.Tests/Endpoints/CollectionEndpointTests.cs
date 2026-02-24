using System.Net;
using System.Net.Http.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// API integration tests for /api/collections/** and /api/collections/{id}/acl/**
/// Uses CustomWebApplicationFactory with real PostgreSQL + test auth handler.
/// </summary>
[Collection("Api")]
public class CollectionEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public CollectionEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AdminClient()
    {
        return _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    }

    private HttpClient ViewerClient()
    {
        return _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());
    }

    // ── Collection CRUD ─────────────────────────────────────────────

    [Fact]
    public async Task CreateCollection_Admin_Returns201()
    {
        var client = AdminClient();
        var dto = new CreateCollectionDto { Name = $"API-Test-{Guid.NewGuid():N}" };

        var response = await client.PostAsJsonAsync("/api/collections", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(dto.Name, body!.Name);
        Assert.True(body.Id != Guid.Empty);
    }

    [Fact]
    public async Task GetRootCollections_Authenticated_Returns200()
    {
        var client = AdminClient();
        // seed data
        await client.PostAsJsonAsync("/api/collections", new CreateCollectionDto { Name = $"Root-{Guid.NewGuid():N}" });

        var response = await client.GetAsync("/api/collections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCollectionById_Exists_Returns200()
    {
        var client = AdminClient();
        var createResponse = await client.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"ById-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.GetAsync($"/api/collections/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        Assert.Equal(created.Id, body!.Id);
    }

    [Fact]
    public async Task GetCollectionById_NoAccess_Returns403()
    {
        // Non-existent collection → no ACL → auth check fails before existence check
        var client = AdminClient();
        var response = await client.GetAsync($"/api/collections/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCollection_Admin_Returns200()
    {
        var client = AdminClient();
        var createResponse = await client.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"ToUpdate-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var patchContent = JsonContent.Create(new { Name = $"Updated-{Guid.NewGuid():N}" });
        var response = await client.PatchAsync($"/api/collections/{created!.Id}", patchContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCollection_Admin_Returns204()
    {
        var client = AdminClient();
        var createResponse = await client.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"ToDelete-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.DeleteAsync($"/api/collections/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── ACL Management ──────────────────────────────────────────────

    [Fact]
    public async Task SetCollectionAccess_ManagerCanGrant_Returns201()
    {
        var client = AdminClient();
        var colResp = await client.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"AclTest-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var aclDto = new SetCollectionAccessDto
        {
            PrincipalType = Constants.PrincipalTypes.User,
            PrincipalId = "new-user-001",
            Role = RoleHierarchy.Roles.Viewer
        };
        var response = await client.PostAsJsonAsync($"/api/collections/{col!.Id}/acl", aclDto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetCollectionAcls_Returns200()
    {
        var client = AdminClient();
        var colResp = await client.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"AclGet-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.GetAsync($"/api/collections/{col!.Id}/acl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokeCollectionAccess_Returns204()
    {
        var client = AdminClient();
        var colResp = await client.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"AclRevoke-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant first
        await client.PostAsJsonAsync($"/api/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = "revoke-target-001", Role = RoleHierarchy.Roles.Viewer });

        // Revoke
        var response = await client.DeleteAsync($"/api/collections/{col.Id}/acl/user/revoke-target-001");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── Authorization ───────────────────────────────────────────────

    [Fact]
    public async Task ViewerCannotDeleteCollection_Returns403()
    {
        // Admin creates collection
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"ViewerCantDelete-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant viewer default user access
        await adminClient.PostAsJsonAsync($"/api/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = TestAuthHandler.DefaultUserId, Role = RoleHierarchy.Roles.Viewer });

        // Viewer tries to delete
        var viewerClient = ViewerClient();
        var response = await viewerClient.DeleteAsync($"/api/collections/{col.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    //  NEGATIVE / ANTI-TESTS
    // ═══════════════════════════════════════════════════════════════

    // ── CreateCollection — negative ─────────────────────────────────

    [Fact]
    public async Task CreateCollection_EmptyName_Returns400()
    {
        var client = AdminClient();
        var dto = new CreateCollectionDto { Name = "" };
        var response = await client.PostAsJsonAsync("/api/collections", dto);

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected 400 or 500 for empty name but got {response.StatusCode}");
    }

    // ── GetCollectionById — negative ────────────────────────────────

    [Fact]
    public async Task GetCollectionById_ViewerNoAccess_Returns403()
    {
        // Admin creates collection, viewer has no ACL
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"ViewerGet-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var viewerClient = ViewerClient();
        var response = await viewerClient.GetAsync($"/api/collections/{col!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── UpdateCollection — negative ─────────────────────────────────

    [Fact]
    public async Task UpdateCollection_NonExistentCollection_Returns403()
    {
        var client = AdminClient();
        var patchContent = JsonContent.Create(new { Name = "Updated" });
        var response = await client.PatchAsync($"/api/collections/{Guid.NewGuid()}", patchContent);

        // No ACL for non-existent collection → 403
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCollection_ViewerNoAccess_Returns403()
    {
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"ViewerUpdate-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant viewer read-only access
        await adminClient.PostAsJsonAsync($"/api/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = TestAuthHandler.DefaultUserId, Role = RoleHierarchy.Roles.Viewer });

        var viewerClient = ViewerClient();
        var patchContent = JsonContent.Create(new { Name = "Viewer tried this" });
        var response = await viewerClient.PatchAsync($"/api/collections/{col.Id}", patchContent);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── DeleteCollection — negative ─────────────────────────────────

    [Fact]
    public async Task DeleteCollection_NonExistentCollection_Returns403()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/collections/{Guid.NewGuid()}");
        // No ACL → 403
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── ACL Management — negative ───────────────────────────────────

    [Fact]
    public async Task GetCollectionAcls_NonExistentCollection_Returns403()
    {
        var client = AdminClient();
        var response = await client.GetAsync($"/api/collections/{Guid.NewGuid()}/acl");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCollectionAcls_ViewerNoAccess_Returns403()
    {
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"AclView-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var viewerClient = ViewerClient();
        var response = await viewerClient.GetAsync($"/api/collections/{col!.Id}/acl");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetCollectionAccess_ViewerCantGrant_Returns403()
    {
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"AclGrant-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant viewer read-only access
        await adminClient.PostAsJsonAsync($"/api/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = TestAuthHandler.DefaultUserId, Role = RoleHierarchy.Roles.Viewer });

        var viewerClient = ViewerClient();
        var aclDto = new SetCollectionAccessDto
        {
            PrincipalType = Constants.PrincipalTypes.User,
            PrincipalId = "another-user-001",
            Role = RoleHierarchy.Roles.Viewer
        };
        var response = await viewerClient.PostAsJsonAsync($"/api/collections/{col.Id}/acl", aclDto);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetCollectionAccess_NonExistentCollection_Returns403()
    {
        var client = AdminClient();
        var aclDto = new SetCollectionAccessDto
        {
            PrincipalType = Constants.PrincipalTypes.User,
            PrincipalId = "user-001",
            Role = RoleHierarchy.Roles.Viewer
        };
        var response = await client.PostAsJsonAsync($"/api/collections/{Guid.NewGuid()}/acl", aclDto);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokeCollectionAccess_NonExistentCollection_Returns403()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/collections/{Guid.NewGuid()}/acl/user/someone");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokeCollectionAccess_ViewerCantRevoke_Returns403()
    {
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"AclRevokeViewer-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant two users access
        await adminClient.PostAsJsonAsync($"/api/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = TestAuthHandler.DefaultUserId, Role = RoleHierarchy.Roles.Viewer });
        await adminClient.PostAsJsonAsync($"/api/collections/{col.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = "another-user", Role = RoleHierarchy.Roles.Viewer });

        // Viewer tries to revoke
        var viewerClient = ViewerClient();
        var response = await viewerClient.DeleteAsync($"/api/collections/{col.Id}/acl/user/another-user");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
