using System.Net;
using System.Net.Http.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// API integration tests for /api/dashboard.
/// </summary>
[Collection("Api")]
public class DashboardEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public DashboardEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetDashboard_Authenticated_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DashboardDto>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task GetDashboard_Admin_IncludesStats()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DashboardDto>();
        Assert.NotNull(body!.Stats);
        Assert.Equal(RoleHierarchy.Roles.Admin, body.UserRole);
    }

    [Fact]
    public async Task GetDashboard_Viewer_NoStats()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DashboardDto>();
        Assert.Null(body!.Stats);
    }
}
