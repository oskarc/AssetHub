using System.Net;
using System.Net.Http.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// API integration tests for /api/admin/** (admin-only endpoints).
/// </summary>
[Collection("Api")]
public class AdminEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AdminEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

    // ── Authorization gate ──────────────────────────────────────────

    [Fact]
    public async Task AdminEndpoints_ViewerForbidden_Shares()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/admin/shares");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_ViewerForbidden_Users()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_ViewerForbidden_CollectionAccess()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/admin/collections/access");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Admin share management ──────────────────────────────────────

    [Fact]
    public async Task GetAllShares_Admin_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/admin/shares");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokeShare_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/admin/shares/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Admin collection access ─────────────────────────────────────

    [Fact]
    public async Task GetCollectionAccessTree_Admin_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/admin/collections/access");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminSetCollectionAccess_ValidCollection_Returns200()
    {
        // Seed a collection
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        var col = TestData.CreateCollection(name: $"AdminAccess-{Guid.NewGuid():N}");
        db.Collections.Add(col);
        await db.SaveChangesAsync();

        var client = AdminClient();
        _factory.MockUserLookup
            .Setup(x => x.GetUserIdByUsernameAsync("testviewer", It.IsAny<CancellationToken>()))
            .ReturnsAsync("resolved-uid-001");

        var request = new SetCollectionAccessRequest
        {
            PrincipalId = "testviewer",
            Role = RoleHierarchy.Roles.Viewer
        };

        var response = await client.PostAsJsonAsync($"/api/admin/collections/{col.Id}/acl", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminSetCollectionAccess_NonExistentCollection_Returns404()
    {
        var client = AdminClient();
        var request = new SetCollectionAccessRequest { PrincipalId = "user-001", Role = RoleHierarchy.Roles.Viewer };

        var response = await client.PostAsJsonAsync($"/api/admin/collections/{Guid.NewGuid()}/acl", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminRemoveCollectionAccess_NonExistentCollection_Returns404()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/admin/collections/{Guid.NewGuid()}/acl/user-001");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Admin user management ───────────────────────────────────────

    [Fact]
    public async Task GetUsers_Admin_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
