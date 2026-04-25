using System.Net;
using System.Net.Http.Json;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AssetHub.Tests.Endpoints;

[Collection("Api")]
public class GuestInvitationEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public GuestInvitationEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

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
        db.GuestInvitations.RemoveRange(db.GuestInvitations);
        db.Collections.RemoveRange(db.Collections);
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

    private async Task<Guid> SeedCollectionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        var col = new Collection
        {
            Id = Guid.NewGuid(),
            Name = $"col-{Guid.NewGuid():N}",
            CreatedByUserId = "admin-test-user-id",
            CreatedAt = DateTime.UtcNow
        };
        db.Collections.Add(col);
        await db.SaveChangesAsync();
        return col.Id;
    }

    [Fact]
    public async Task List_NonAdmin_Returns403()
    {
        var response = await ViewerClient().GetAsync("/api/v1/admin/guest-invitations");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateListRoundTrip_ReturnsMagicLinkOnce()
    {
        var collectionId = await SeedCollectionAsync();
        _factory.MockKeycloak.Reset();
        _factory.MockEmail.Reset();
        var client = AdminClient();

        var dto = new CreateGuestInvitationDto
        {
            Email = "Reviewer@example.com",
            CollectionIds = [collectionId],
            ExpiresInDays = 14
        };

        var create = await client.PostAsJsonAsync("/api/v1/admin/guest-invitations", dto);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreatedGuestInvitationDto>();
        Assert.NotNull(created);
        Assert.Contains("/guest-accept?token=", created!.MagicLinkUrl);
        Assert.Equal("reviewer@example.com", created.Invitation.Email); // lowercased
        Assert.Equal("pending", created.Invitation.Status);

        _factory.MockEmail.Verify(e => e.SendEmailAsync(
            "reviewer@example.com",
            It.IsAny<AssetHub.Application.Services.Email.Templates.GuestInvitationEmailTemplate>(),
            It.IsAny<CancellationToken>()), Times.Once);

        var list = await client.GetAsync("/api/v1/admin/guest-invitations");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = await list.Content.ReadFromJsonAsync<List<GuestInvitationResponseDto>>();
        Assert.Single(items!);
        Assert.Equal(created.Invitation.Id, items![0].Id);
    }

    [Fact]
    public async Task Create_UnknownCollection_Returns400()
    {
        var client = AdminClient();
        var dto = new CreateGuestInvitationDto
        {
            Email = "x@example.com",
            CollectionIds = [Guid.NewGuid()] // not seeded
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/guest-invitations", dto);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Accept_BadToken_Returns404()
    {
        // Anonymous client — no auth header.
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/guest-invitations/accept", new { Token = "garbage-token" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_AfterCreate_StatusBecomesRevoked()
    {
        var collectionId = await SeedCollectionAsync();
        _factory.MockKeycloak.Reset();
        _factory.MockEmail.Reset();
        var client = AdminClient();

        var create = await client.PostAsJsonAsync("/api/v1/admin/guest-invitations",
            new CreateGuestInvitationDto
            {
                Email = "x@example.com",
                CollectionIds = [collectionId]
            });
        var created = await create.Content.ReadFromJsonAsync<CreatedGuestInvitationDto>();

        var revoke = await client.PostAsync(
            $"/api/v1/admin/guest-invitations/{created!.Invitation.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var list = await client.GetAsync("/api/v1/admin/guest-invitations");
        var items = await list.Content.ReadFromJsonAsync<List<GuestInvitationResponseDto>>();
        Assert.Single(items!);
        Assert.Equal("revoked", items![0].Status);
    }
}
