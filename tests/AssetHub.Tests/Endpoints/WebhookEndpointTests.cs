using System.Net;
using System.Net.Http.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

[Collection("Api")]
public class WebhookEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public WebhookEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

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
        db.WebhookDeliveries.RemoveRange(db.WebhookDeliveries);
        db.Webhooks.RemoveRange(db.Webhooks);
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

    [Fact]
    public async Task List_NonAdmin_Returns403()
    {
        var response = await ViewerClient().GetAsync("/api/v1/admin/webhooks");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateListRoundTrip_Admin_Succeeds_AndReturnsPlaintextOnce()
    {
        var client = AdminClient();
        var dto = new CreateWebhookDto
        {
            Name = "Test hook",
            Url = "https://example.test/webhook",
            EventTypes = new List<string> { WebhookEvents.AssetCreated, WebhookEvents.WorkflowStateChanged }
        };

        var create = await client.PostAsJsonAsync("/api/v1/admin/webhooks", dto);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreatedWebhookDto>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.PlaintextSecret));
        Assert.Equal("Test hook", created.Webhook.Name);

        var list = await client.GetAsync("/api/v1/admin/webhooks");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = await list.Content.ReadFromJsonAsync<List<WebhookResponseDto>>();
        Assert.Single(items!);
        Assert.Equal(created.Webhook.Id, items![0].Id);
    }

    [Fact]
    public async Task RotateSecret_ReturnsNewPlaintext()
    {
        var client = AdminClient();
        var first = await client.PostAsJsonAsync("/api/v1/admin/webhooks", new CreateWebhookDto
        {
            Name = "x",
            Url = "https://example.test/h",
            EventTypes = new List<string> { WebhookEvents.AssetCreated }
        });
        var initial = await first.Content.ReadFromJsonAsync<CreatedWebhookDto>();

        var rotated = await client.PostAsync(
            $"/api/v1/admin/webhooks/{initial!.Webhook.Id}/rotate-secret", content: null);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var rotatedDto = await rotated.Content.ReadFromJsonAsync<CreatedWebhookDto>();
        Assert.NotEqual(initial.PlaintextSecret, rotatedDto!.PlaintextSecret);
    }

    [Fact]
    public async Task Create_UnknownEventType_Returns400()
    {
        var client = AdminClient();
        var dto = new CreateWebhookDto
        {
            Name = "x",
            Url = "https://example.test/h",
            EventTypes = new List<string> { "not.a.real.event" }
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/webhooks", dto);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
