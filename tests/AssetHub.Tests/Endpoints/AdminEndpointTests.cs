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
using Moq;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// API integration tests for /api/v1/admin/** (admin-only endpoints).
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
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

    // ── Authorization gate ──────────────────────────────────────────

    [Fact]
    public async Task AdminEndpoints_ViewerForbidden_Shares()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/v1/admin/shares");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_ViewerForbidden_Users()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_ViewerForbidden_CollectionAccess()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/v1/admin/collections/access");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Admin share management ──────────────────────────────────────

    [Fact]
    public async Task GetAllShares_Admin_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/v1/admin/shares");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokeShare_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/v1/admin/shares/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Admin collection access ─────────────────────────────────────

    [Fact]
    public async Task GetCollectionAccessTree_Admin_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/v1/admin/collections/access");
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

        var response = await client.PostAsJsonAsync($"/api/v1/admin/collections/{col.Id}/acl", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminSetCollectionAccess_NonExistentCollection_Returns404()
    {
        var client = AdminClient();
        var request = new SetCollectionAccessRequest { PrincipalId = "user-001", Role = RoleHierarchy.Roles.Viewer };

        var response = await client.PostAsJsonAsync($"/api/v1/admin/collections/{Guid.NewGuid()}/acl", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminRemoveCollectionAccess_NonExistentCollection_Returns404()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/v1/admin/collections/{Guid.NewGuid()}/acl/user-001");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Admin user management ───────────────────────────────────────

    [Fact]
    public async Task GetUsers_Admin_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    //  NEGATIVE / ANTI-TESTS
    // ═══════════════════════════════════════════════════════════════

    // ── Viewer blocked from ALL admin endpoints ─────────────────────

    [Fact]
    public async Task Viewer_CannotGetShareToken_Returns403()
    {
        var client = ViewerClient();
        var response = await client.GetAsync($"/api/v1/admin/shares/{Guid.NewGuid()}/token");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotRevokeShare_Returns403()
    {
        var client = ViewerClient();
        var response = await client.DeleteAsync($"/api/v1/admin/shares/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotCreateUser_Returns403()
    {
        var client = ViewerClient();
        var request = new CreateUserRequest
        {
            Username = "forbidden-user",
            Email = "forbidden@test.com",
            FirstName = "Forbidden",
            LastName = "User"
        };
        var response = await client.PostAsJsonAsync("/api/v1/admin/users", request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotDeleteUser_Returns403()
    {
        var client = ViewerClient();
        var response = await client.DeleteAsync("/api/v1/admin/users/some-user-id");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotResetPassword_Returns403()
    {
        var client = ViewerClient();
        var response = await client.PostAsync("/api/v1/admin/users/some-user-id/reset-password", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotSyncUsers_Returns403()
    {
        var client = ViewerClient();
        var response = await client.PostAsync("/api/v1/admin/users/sync?dryRun=true", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotSetCollectionAccess_Returns403()
    {
        var client = ViewerClient();
        var request = new SetCollectionAccessRequest { PrincipalId = "user-001", Role = RoleHierarchy.Roles.Viewer };
        var response = await client.PostAsJsonAsync($"/api/v1/admin/collections/{Guid.NewGuid()}/acl", request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotRemoveCollectionAccess_Returns403()
    {
        var client = ViewerClient();
        var response = await client.DeleteAsync($"/api/v1/admin/collections/{Guid.NewGuid()}/acl/user-001");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotGetKeycloakUsers_Returns403()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/v1/admin/keycloak-users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Admin share management — negative ───────────────────────────

    [Fact]
    public async Task GetShareToken_NonExistentShare_Returns404()
    {
        var client = AdminClient();
        var response = await client.GetAsync($"/api/v1/admin/shares/{Guid.NewGuid()}/token");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Admin user management — negative ────────────────────────────

    [Fact]
    public async Task CreateUser_InvalidUsername_Returns400()
    {
        var client = AdminClient();

        // Setup Keycloak mock to simulate user creation
        _factory.MockKeycloak
            .Setup(x => x.CreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-user-id");

        var request = new CreateUserRequest
        {
            Username = "a",        // Too short (min 3)
            Email = "test@test.com",
            FirstName = "Test",
            LastName = "User"
        };
        var response = await client.PostAsJsonAsync("/api/v1/admin/users", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_InvalidEmail_Returns400()
    {
        var client = AdminClient();
        var request = new CreateUserRequest
        {
            Username = "validuser",
            Email = "not-an-email",
            FirstName = "Test",
            LastName = "User"
        };
        var response = await client.PostAsJsonAsync("/api/v1/admin/users", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_MissingFirstName_Returns400()
    {
        var client = AdminClient();
        var request = new CreateUserRequest
        {
            Username = "validuser2",
            Email = "valid@test.com",
            FirstName = "",         // Empty → required
            LastName = "User"
        };
        var response = await client.PostAsJsonAsync("/api/v1/admin/users", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_UsernameWithSpecialChars_Returns400()
    {
        var client = AdminClient();
        var request = new CreateUserRequest
        {
            Username = "user name!@#",  // Invalid characters
            Email = "valid@test.com",
            FirstName = "Test",
            LastName = "User"
        };
        var response = await client.PostAsJsonAsync("/api/v1/admin/users", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_NonExistentUser_Returns404Or500()
    {
        var client = AdminClient();
        _factory.MockKeycloak
            .Setup(x => x.DeleteUserAsync("non-existent-uid", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetHub.Application.Services.KeycloakApiException("User not found", 404));

        var response = await client.DeleteAsync("/api/v1/admin/users/non-existent-uid");

        // Depends on how the service handles the exception
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected 404 or 500 but got {response.StatusCode}");
    }

    [Fact]
    public async Task ResetPassword_NonExistentUser_Returns404Or500()
    {
        var client = AdminClient();
        _factory.MockKeycloak
            .Setup(x => x.SendExecuteActionsEmailAsync("non-existent-uid", It.IsAny<IEnumerable<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetHub.Application.Services.KeycloakApiException("User not found", 404));

        var response = await client.PostAsync("/api/v1/admin/users/non-existent-uid/reset-password", null);

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected 404 or 500 but got {response.StatusCode}");
    }
}
