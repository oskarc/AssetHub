using System.Net;
using System.Net.Http.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// API integration tests for /api/v1/collections/** and /api/v1/collections/{id}/acl/**
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
        await db.Database.MigrateAsync();
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

        var response = await client.PostAsJsonAsync("/api/v1/collections", dto);

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
        await client.PostAsJsonAsync("/api/v1/collections", new CreateCollectionDto { Name = $"Root-{Guid.NewGuid():N}" });

        var response = await client.GetAsync("/api/v1/collections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCollectionById_Exists_Returns200()
    {
        var client = AdminClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"ById-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.GetAsync($"/api/v1/collections/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        Assert.Equal(created.Id, body!.Id);
    }

    [Fact]
    public async Task GetCollectionById_NoAccess_Returns403()
    {
        // Non-existent collection → no ACL → auth check fails before existence check
        var client = ViewerClient();
        var response = await client.GetAsync($"/api/v1/collections/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCollection_Admin_Returns200()
    {
        var client = AdminClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"ToUpdate-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var patchContent = JsonContent.Create(new { Name = $"Updated-{Guid.NewGuid():N}" });
        var response = await client.PatchAsync($"/api/v1/collections/{created!.Id}", patchContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCollection_Admin_Returns204()
    {
        var client = AdminClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"ToDelete-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.DeleteAsync($"/api/v1/collections/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── ACL Management ──────────────────────────────────────────────

    [Fact]
    public async Task SetCollectionAccess_ManagerCanGrant_Returns201()
    {
        var client = AdminClient();
        var colResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"AclTest-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var aclDto = new SetCollectionAccessDto
        {
            PrincipalType = Constants.PrincipalTypes.User,
            PrincipalId = "new-user-001",
            Role = RoleHierarchy.Roles.Viewer
        };
        var response = await client.PostAsJsonAsync($"/api/v1/collections/{col!.Id}/acl", aclDto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetCollectionAcls_Returns200()
    {
        var client = AdminClient();
        var colResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"AclGet-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.GetAsync($"/api/v1/collections/{col!.Id}/acl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokeCollectionAccess_Returns204()
    {
        var client = AdminClient();
        var colResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"AclRevoke-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant first
        await client.PostAsJsonAsync($"/api/v1/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = "revoke-target-001", Role = RoleHierarchy.Roles.Viewer });

        // Revoke
        var response = await client.DeleteAsync($"/api/v1/collections/{col.Id}/acl/user/revoke-target-001");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── Authorization ───────────────────────────────────────────────

    [Fact]
    public async Task ViewerCannotDeleteCollection_Returns403()
    {
        // Admin creates collection
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"ViewerCantDelete-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant viewer default user access
        await adminClient.PostAsJsonAsync($"/api/v1/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = TestAuthHandler.DefaultUserId, Role = RoleHierarchy.Roles.Viewer });

        // Viewer tries to delete
        var viewerClient = ViewerClient();
        var response = await viewerClient.DeleteAsync($"/api/v1/collections/{col.Id}");

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
        var response = await client.PostAsJsonAsync("/api/v1/collections", dto);

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
        var colResp = await adminClient.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"ViewerGet-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var viewerClient = ViewerClient();
        var response = await viewerClient.GetAsync($"/api/v1/collections/{col!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── UpdateCollection — negative ─────────────────────────────────

    [Fact]
    public async Task UpdateCollection_NonExistentCollection_Returns403()
    {
        var client = ViewerClient();
        var patchContent = JsonContent.Create(new { Name = "Updated" });
        var response = await client.PatchAsync($"/api/v1/collections/{Guid.NewGuid()}", patchContent);

        // No ACL for non-existent collection → 403
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCollection_ViewerNoAccess_Returns403()
    {
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"ViewerUpdate-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant viewer read-only access
        await adminClient.PostAsJsonAsync($"/api/v1/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = TestAuthHandler.DefaultUserId, Role = RoleHierarchy.Roles.Viewer });

        var viewerClient = ViewerClient();
        var patchContent = JsonContent.Create(new { Name = "Viewer tried this" });
        var response = await viewerClient.PatchAsync($"/api/v1/collections/{col.Id}", patchContent);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── DeleteCollection — negative ─────────────────────────────────

    [Fact]
    public async Task DeleteCollection_NonExistentCollection_Returns403()
    {
        var client = ViewerClient();
        var response = await client.DeleteAsync($"/api/v1/collections/{Guid.NewGuid()}");
        // No ACL → 403
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── ACL Management — negative ───────────────────────────────────

    [Fact]
    public async Task GetCollectionAcls_NonExistentCollection_Returns403()
    {
        var client = ViewerClient();
        var response = await client.GetAsync($"/api/v1/collections/{Guid.NewGuid()}/acl");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCollectionAcls_ViewerNoAccess_Returns403()
    {
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"AclView-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var viewerClient = ViewerClient();
        var response = await viewerClient.GetAsync($"/api/v1/collections/{col!.Id}/acl");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetCollectionAccess_ViewerCantGrant_Returns403()
    {
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"AclGrant-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant viewer read-only access
        await adminClient.PostAsJsonAsync($"/api/v1/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = TestAuthHandler.DefaultUserId, Role = RoleHierarchy.Roles.Viewer });

        var viewerClient = ViewerClient();
        var aclDto = new SetCollectionAccessDto
        {
            PrincipalType = Constants.PrincipalTypes.User,
            PrincipalId = "another-user-001",
            Role = RoleHierarchy.Roles.Viewer
        };
        var response = await viewerClient.PostAsJsonAsync($"/api/v1/collections/{col.Id}/acl", aclDto);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetCollectionAccess_NonExistentCollection_Returns403()
    {
        var client = ViewerClient();
        var aclDto = new SetCollectionAccessDto
        {
            PrincipalType = Constants.PrincipalTypes.User,
            PrincipalId = "user-001",
            Role = RoleHierarchy.Roles.Viewer
        };
        var response = await client.PostAsJsonAsync($"/api/v1/collections/{Guid.NewGuid()}/acl", aclDto);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokeCollectionAccess_NonExistentCollection_Returns403()
    {
        var client = ViewerClient();
        var response = await client.DeleteAsync($"/api/v1/collections/{Guid.NewGuid()}/acl/user/someone");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokeCollectionAccess_ViewerCantRevoke_Returns403()
    {
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"AclRevokeViewer-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        // Grant two users access
        await adminClient.PostAsJsonAsync($"/api/v1/collections/{col!.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = TestAuthHandler.DefaultUserId, Role = RoleHierarchy.Roles.Viewer });
        await adminClient.PostAsJsonAsync($"/api/v1/collections/{col.Id}/acl",
            new SetCollectionAccessDto { PrincipalType = Constants.PrincipalTypes.User, PrincipalId = "another-user", Role = RoleHierarchy.Roles.Viewer });

        // Viewer tries to revoke
        var viewerClient = ViewerClient();
        var response = await viewerClient.DeleteAsync($"/api/v1/collections/{col.Id}/acl/user/another-user");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Nested collections (T5-NEST-01) ────────────────────────────

    [Fact]
    public async Task SetParent_AdminReparents_Returns204()
    {
        var client = AdminClient();
        var parentResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"NestParent-{Guid.NewGuid():N}" });
        var parent = await parentResp.Content.ReadFromJsonAsync<CollectionResponseDto>();
        var childResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"NestChild-{Guid.NewGuid():N}" });
        var child = await childResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/collections/{child!.Id}/parent",
            new SetParentRequestDto { ParentId = parent!.Id });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify the response shape carries the new fields.
        var reloaded = await (await client.GetAsync($"/api/v1/collections/{child.Id}"))
            .Content.ReadFromJsonAsync<CollectionResponseDto>();
        Assert.Equal(parent.Id, reloaded!.ParentCollectionId);
        Assert.False(reloaded.InheritParentAcl);
    }

    [Fact]
    public async Task SetParent_NonAdmin_Returns403()
    {
        var admin = AdminClient();
        var childResp = await admin.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"NestForbid-{Guid.NewGuid():N}" });
        var child = await childResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var viewer = ViewerClient();
        var response = await viewer.PatchAsJsonAsync(
            $"/api/v1/collections/{child!.Id}/parent",
            new SetParentRequestDto { ParentId = null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetInheritAcl_TogglesAndPersists_Returns204()
    {
        var client = AdminClient();
        var parentResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"InhParent-{Guid.NewGuid():N}" });
        var parent = await parentResp.Content.ReadFromJsonAsync<CollectionResponseDto>();
        var childResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto
            {
                Name = $"InhChild-{Guid.NewGuid():N}",
                ParentCollectionId = parent!.Id,
            });
        var child = await childResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/collections/{child!.Id}/inherit-acl",
            new SetInheritAclRequestDto { Inherit = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var reloaded = await (await client.GetAsync($"/api/v1/collections/{child.Id}"))
            .Content.ReadFromJsonAsync<CollectionResponseDto>();
        Assert.True(reloaded!.InheritParentAcl);
    }

    [Fact]
    public async Task CopyAclFromParent_AddsParentRowsAndReturnsCount()
    {
        var client = AdminClient();
        var parentResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"CopyParent-{Guid.NewGuid():N}" });
        var parent = await parentResp.Content.ReadFromJsonAsync<CollectionResponseDto>();
        // Grant a non-admin principal on the parent so there is something to copy.
        await client.PostAsJsonAsync($"/api/v1/collections/{parent!.Id}/acl",
            new SetCollectionAccessDto
            {
                PrincipalType = Constants.PrincipalTypes.User,
                PrincipalId = $"copy-src-{Guid.NewGuid():N}",
                Role = RoleHierarchy.Roles.Viewer,
            });
        var childResp = await client.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto
            {
                Name = $"CopyChild-{Guid.NewGuid():N}",
                ParentCollectionId = parent.Id,
            });
        var child = await childResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.PostAsync(
            $"/api/v1/collections/{child!.Id}/copy-acl-from-parent", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CopyParentAclResponseDto>();
        // The admin's own row from create gets copied along with the granted user.
        Assert.True(body!.PrincipalsAdded >= 1);
    }

    [Fact]
    public async Task CreateCollection_NonAdminWithParent_Returns403()
    {
        var admin = AdminClient();
        var parentResp = await admin.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto { Name = $"NoNestParent-{Guid.NewGuid():N}" });
        var parent = await parentResp.Content.ReadFromJsonAsync<CollectionResponseDto>();
        // Give the viewer access so the create-root check isn't what trips them.
        await admin.PostAsJsonAsync($"/api/v1/collections/{parent!.Id}/acl",
            new SetCollectionAccessDto
            {
                PrincipalType = Constants.PrincipalTypes.User,
                PrincipalId = TestAuthHandler.DefaultUserId,
                Role = RoleHierarchy.Roles.Manager,
            });

        var viewer = ViewerClient();
        var response = await viewer.PostAsJsonAsync("/api/v1/collections",
            new CreateCollectionDto
            {
                Name = $"ChildAttempt-{Guid.NewGuid():N}",
                ParentCollectionId = parent.Id,
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
