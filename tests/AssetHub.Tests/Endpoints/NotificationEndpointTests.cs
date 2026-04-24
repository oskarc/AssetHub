using System.Net;
using System.Net.Http.Json;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

[Collection("Api")]
public class NotificationEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public NotificationEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

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
        db.Notifications.RemoveRange(db.Notifications);
        db.NotificationPreferences.RemoveRange(db.NotificationPreferences);
        await db.SaveChangesAsync();
    }

    private const string Base = "/api/v1/notifications";

    private HttpClient AuthenticatedClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());
    private HttpClient AnonymousClient()
    {
        TestAuthHandler.ClaimsOverride = null;
        return _factory.CreateClient();
    }

    // Seeds a notification directly via the DB (bypasses the service so endpoint
    // tests don't depend on a write endpoint that doesn't exist — notifications
    // are produced by other features, not by the user).
    private async Task<Guid> SeedNotificationAsync(string userId, string title = "Hello")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        var notif = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Category = "mention",
            Title = title,
            CreatedAt = DateTime.UtcNow
        };
        db.Notifications.Add(notif);
        await db.SaveChangesAsync();
        return notif.Id;
    }

    // ── Auth gating ─────────────────────────────────────────────────

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var response = await AnonymousClient().GetAsync(Base);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnreadCount_Unauthenticated_Returns401()
    {
        var response = await AnonymousClient().GetAsync($"{Base}/unread-count");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preferences_Unauthenticated_Returns401()
    {
        var response = await AnonymousClient().GetAsync($"{Base}/preferences");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── List / unread count ─────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsCurrentUsersNotifications()
    {
        var client = AuthenticatedClient();
        await SeedNotificationAsync(TestAuthHandler.DefaultUserId, "a");
        await SeedNotificationAsync(TestAuthHandler.DefaultUserId, "b");
        await SeedNotificationAsync("someone-else", "hidden");

        var body = await client.GetFromJsonAsync<NotificationListResponse>(Base);

        Assert.NotNull(body);
        Assert.Equal(2, body!.TotalCount);
        Assert.DoesNotContain(body.Items, i => i.Title == "hidden");
    }

    [Fact]
    public async Task UnreadCount_Returns200AndCount()
    {
        var client = AuthenticatedClient();
        await SeedNotificationAsync(TestAuthHandler.DefaultUserId);
        await SeedNotificationAsync(TestAuthHandler.DefaultUserId);

        var body = await client.GetFromJsonAsync<NotificationUnreadCountDto>($"{Base}/unread-count");

        Assert.NotNull(body);
        Assert.Equal(2, body!.Count);
    }

    // ── Mark read / mark all read / delete ──────────────────────────

    [Fact]
    public async Task MarkRead_OwnNotification_Returns200()
    {
        var client = AuthenticatedClient();
        var id = await SeedNotificationAsync(TestAuthHandler.DefaultUserId);

        var response = await client.PostAsync($"{Base}/{id}/read", null);

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
    }

    [Fact]
    public async Task MarkRead_OtherUsersNotification_Returns404()
    {
        var client = AuthenticatedClient();
        var id = await SeedNotificationAsync("someone-else");

        var response = await client.PostAsync($"{Base}/{id}/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarkAllRead_Returns200AndAffectedCount()
    {
        var client = AuthenticatedClient();
        await SeedNotificationAsync(TestAuthHandler.DefaultUserId);
        await SeedNotificationAsync(TestAuthHandler.DefaultUserId);

        var response = await client.PostAsync($"{Base}/read-all", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var count = await response.Content.ReadFromJsonAsync<int>();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Delete_OwnNotification_Returns200()
    {
        var client = AuthenticatedClient();
        var id = await SeedNotificationAsync(TestAuthHandler.DefaultUserId);

        var response = await client.DeleteAsync($"{Base}/{id}");

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        Assert.Null(await db.Notifications.FindAsync(id));
    }

    // ── Preferences ─────────────────────────────────────────────────

    [Fact]
    public async Task Preferences_FirstCall_LazilyCreatesWithDefaults()
    {
        var client = AuthenticatedClient();

        var body = await client.GetFromJsonAsync<NotificationPreferencesDto>($"{Base}/preferences");

        Assert.NotNull(body);
        Assert.True(body!.Categories["mention"].InApp);
        Assert.Equal("instant", body.Categories["saved_search_digest"].EmailCadence);
    }

    [Fact]
    public async Task Preferences_Update_PersistsChangesAndEchoes()
    {
        var client = AuthenticatedClient();
        await client.GetAsync($"{Base}/preferences"); // ensure row exists

        var dto = new UpdateNotificationPreferencesDto
        {
            Categories = new Dictionary<string, NotificationCategoryPrefsDto>
            {
                ["mention"] = new() { InApp = true, Email = false, EmailCadence = "instant" }
            }
        };

        var response = await client.PutAsJsonAsync($"{Base}/preferences", dto);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<NotificationPreferencesDto>();
        Assert.False(body!.Categories["mention"].Email);

        // Re-GET confirms persistence.
        var refreshed = await client.GetFromJsonAsync<NotificationPreferencesDto>($"{Base}/preferences");
        Assert.False(refreshed!.Categories["mention"].Email);
    }

    [Fact]
    public async Task Preferences_Update_InvalidCadence_Returns400()
    {
        var client = AuthenticatedClient();
        // Bypass the DataAnnotations regex by directly constructing an invalid cadence.
        // ValidationFilter catches it first, returning 400.
        var dto = new UpdateNotificationPreferencesDto
        {
            Categories = new Dictionary<string, NotificationCategoryPrefsDto>
            {
                ["mention"] = new() { InApp = true, Email = true, EmailCadence = "hourly" }
            }
        };

        var response = await client.PutAsJsonAsync($"{Base}/preferences", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Anonymous unsubscribe via signed token ─────────────────────────

    [Fact]
    public async Task Unsubscribe_MissingToken_ReturnsBadRequest()
    {
        var response = await AnonymousClient().GetAsync($"{Base}/unsubscribe");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unsubscribe_InvalidToken_Returns200WithNeutralBody()
    {
        // Neutral response by design: the endpoint does not distinguish
        // invalid-token / unknown-user / stamp-mismatch so an attacker cannot
        // probe for valid ids. The service layer surfaces Applied=false +
        // Category=null and the endpoint renders the "not valid" page at 200.
        var response = await AnonymousClient().GetAsync($"{Base}/unsubscribe?token=not-a-real-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unsubscribe link not valid", body);
    }

    [Fact]
    public async Task Unsubscribe_ValidToken_FlipsEmailAndReturnsConfirmationHtml()
    {
        var userId = TestAuthHandler.DefaultUserId;

        // Bootstrap prefs row (GET preferences lazy-creates) + mint a real
        // signed token via the app's DI so the Data Protection key ring
        // matches the endpoint we're hitting.
        var authClient = AuthenticatedClient();
        var prefsResponse = await authClient.GetAsync($"{Base}/preferences");
        Assert.Equal(HttpStatusCode.OK, prefsResponse.StatusCode);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<Application.Services.INotificationUnsubscribeTokenService>();
            var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
            var row = await db.NotificationPreferences.FirstAsync(p => p.UserId == userId);
            token = tokens.CreateToken(userId, "mention", row.UnsubscribeTokenHash);
        }

        var response = await AnonymousClient().GetAsync(
            $"{Base}/unsubscribe?token={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unsubscribed", body);

        // Confirm persistence.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
            var row = await db.NotificationPreferences.FirstAsync(p => p.UserId == userId);
            Assert.True(row.Categories.TryGetValue("mention", out var cat));
            Assert.False(cat!.Email);
        }
    }
}
