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

/// <summary>
/// HTTP integration tests for /api/v1/assets/{id}/comments. Uses the real
/// service + Postgres fixture; verifies auth gating at the group level and
/// the happy-path round trip.
/// </summary>
[Collection("Api")]
public class AssetCommentEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AssetCommentEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

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
        db.AssetComments.RemoveRange(db.AssetComments);
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient AnonymousClient()
    {
        TestAuthHandler.ClaimsOverride = null;
        return _factory.CreateClient();
    }

    private async Task<(Guid ColId, Guid AssetId)> SeedAssetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();

        var col = TestData.CreateCollection(name: $"Col-{Guid.NewGuid():N}", createdByUserId: TestAuthHandler.AdminUserId);
        var asset = TestData.CreateAsset(title: "comment-test", createdByUserId: TestAuthHandler.AdminUserId);
        db.Collections.Add(col);
        db.Assets.Add(asset);
        db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id, addedByUserId: TestAuthHandler.AdminUserId));
        db.CollectionAcls.Add(TestData.CreateAcl(col.Id, TestAuthHandler.AdminUserId, AclRole.Admin));
        await db.SaveChangesAsync();
        return (col.Id, asset.Id);
    }

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        var response = await AnonymousClient().GetAsync($"/api/v1/assets/{Guid.NewGuid()}/comments");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateListRoundTrip_Admin_Succeeds()
    {
        var (_, assetId) = await SeedAssetAsync();
        var client = AdminClient();

        var create = await client.PostAsJsonAsync($"/api/v1/assets/{assetId}/comments",
            new CreateAssetCommentDto { Body = "hello world" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<AssetCommentResponseDto>();
        Assert.NotNull(created);
        Assert.Equal("hello world", created!.Body);

        var list = await client.GetAsync($"/api/v1/assets/{assetId}/comments");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = await list.Content.ReadFromJsonAsync<List<AssetCommentResponseDto>>();
        Assert.Single(items!);
        Assert.Equal(created.Id, items![0].Id);
    }

    [Fact]
    public async Task Create_MissingBody_Returns400()
    {
        var (_, assetId) = await SeedAssetAsync();
        var client = AdminClient();

        var response = await client.PostAsJsonAsync($"/api/v1/assets/{assetId}/comments",
            new CreateAssetCommentDto { Body = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Author_Succeeds()
    {
        var (_, assetId) = await SeedAssetAsync();
        var client = AdminClient();

        var create = await client.PostAsJsonAsync($"/api/v1/assets/{assetId}/comments",
            new CreateAssetCommentDto { Body = "original" });
        var created = await create.Content.ReadFromJsonAsync<AssetCommentResponseDto>();

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/assets/{assetId}/comments/{created!.Id}",
            new UpdateAssetCommentDto { Body = "edited" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var updated = await patch.Content.ReadFromJsonAsync<AssetCommentResponseDto>();
        Assert.Equal("edited", updated!.Body);
        Assert.NotNull(updated.EditedAt);
    }

    [Fact]
    public async Task Delete_Author_Succeeds()
    {
        var (_, assetId) = await SeedAssetAsync();
        var client = AdminClient();

        var create = await client.PostAsJsonAsync($"/api/v1/assets/{assetId}/comments",
            new CreateAssetCommentDto { Body = "will be deleted" });
        var created = await create.Content.ReadFromJsonAsync<AssetCommentResponseDto>();

        var del = await client.DeleteAsync($"/api/v1/assets/{assetId}/comments/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await client.GetAsync($"/api/v1/assets/{assetId}/comments");
        var items = await list.Content.ReadFromJsonAsync<List<AssetCommentResponseDto>>();
        Assert.Empty(items!);
    }
}
