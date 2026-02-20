using System.Net;
using System.Net.Http.Json;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AssetHub.Tests.Endpoints;

/// <summary>DelegatingHandler that prevents following redirects (for testing 302 responses).</summary>
internal class RedirectHandler : DelegatingHandler
{
    public RedirectHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
}

/// <summary>
/// API integration tests for /api/assets/**
/// Uses CustomWebApplicationFactory with real PostgreSQL + mocked MinIO.
/// </summary>
[Collection("Api")]
public class AssetEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AssetEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

    /// <summary>Seeds a collection + asset via DB and returns (collectionId, assetId).</summary>
    private async Task<(Guid ColId, Guid AssetId)> SeedCollectionWithAssetAsync(
        string userId = TestAuthHandler.AdminUserId,
        AclRole role = AclRole.Admin)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();

        var col = TestData.CreateCollection(name: $"Col-{Guid.NewGuid():N}", createdByUserId: userId);
        var asset = TestData.CreateAsset(title: $"Asset-{Guid.NewGuid():N}", createdByUserId: userId);
        db.Collections.Add(col);
        db.Assets.Add(asset);
        db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id, addedByUserId: userId));
        db.CollectionAcls.Add(TestData.CreateAcl(col.Id, userId, role));
        await db.SaveChangesAsync();

        return (col.Id, asset.Id);
    }

    // ── GetAssets (admin-only) ──────────────────────────────────────

    [Fact]
    public async Task GetAssets_AdminOnly_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/assets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAssets_Viewer_Returns403()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/assets");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAllAssets_AdminOnly_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/assets/all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAllAssets_Viewer_Returns403()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/assets/all");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GetAsset ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsset_WithAccess_Returns200()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();

        var response = await client.GetAsync($"/api/assets/{assetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponseDto>();
        Assert.Equal(assetId, body!.Id);
    }

    [Fact]
    public async Task GetAsset_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.GetAsync($"/api/assets/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAsset_NoAccess_Returns403()
    {
        // Seed with admin user — viewer has no ACL
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = ViewerClient();

        var response = await client.GetAsync($"/api/assets/{assetId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GetAssetsByCollection ───────────────────────────────────────

    [Fact]
    public async Task GetAssetsByCollection_WithAccess_Returns200()
    {
        var (colId, _) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();

        var response = await client.GetAsync($"/api/assets/collection/{colId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── UpdateAsset ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsset_WithContributorAccess_Returns200()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();

        var patchContent = JsonContent.Create(new { Title = "Updated Title" });
        var response = await client.PatchAsync($"/api/assets/{assetId}", patchContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── DeleteAsset ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsset_Admin_Returns204()
    {
        var (colId, assetId) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();

        var response = await client.DeleteAsync($"/api/assets/{assetId}?fromCollectionId={colId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAsset_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/assets/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Renditions ──────────────────────────────────────────────────

    [Fact]
    public async Task GetThumbnail_WithAccess_Returns302()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();
        // Don't follow redirects for this test
        var noRedirectClient = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await noRedirectClient.GetAsync($"/api/assets/{assetId}/thumb");

        // Should redirect to presigned URL
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task DownloadOriginal_WithAccess_Returns302()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await client.GetAsync($"/api/assets/{assetId}/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // ── Init Presigned Upload ───────────────────────────────────────

    [Fact]
    public async Task InitUpload_WithAccess_Returns200()
    {
        // Create a collection first for the upload target
        var adminClient = AdminClient();
        var colResp = await adminClient.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"Upload-{Guid.NewGuid():N}" });
        var col = await colResp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var request = new InitUploadRequest
        {
            CollectionId = col!.Id,
            FileName = "test-image.jpg",
            ContentType = "image/jpeg",
            FileSize = 1024,
            Title = "Test Upload"
        };

        var response = await adminClient.PostAsJsonAsync("/api/assets/init-upload", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<InitUploadResponse>();
        Assert.NotNull(body);
        Assert.True(body!.AssetId != Guid.Empty);
        Assert.False(string.IsNullOrEmpty(body.UploadUrl));
    }

    // ── Multi-Collection ────────────────────────────────────────────

    [Fact]
    public async Task GetAssetCollections_Returns200()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();

        var response = await client.GetAsync($"/api/assets/{assetId}/collections");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddAssetToCollection_Returns201()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();

        // Create second collection
        var col2Resp = await client.PostAsJsonAsync("/api/collections",
            new CreateCollectionDto { Name = $"Second-{Guid.NewGuid():N}" });
        var col2 = await col2Resp.Content.ReadFromJsonAsync<CollectionResponseDto>();

        var response = await client.PostAsync($"/api/assets/{assetId}/collections/{col2!.Id}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── GetDeletionContext ──────────────────────────────────────────

    [Fact]
    public async Task GetDeletionContext_Returns200()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();

        var response = await client.GetAsync($"/api/assets/{assetId}/deletion-context");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetDeletionContextDto>();
        Assert.NotNull(body);
        Assert.Equal(1, body!.CollectionCount);
        Assert.True(body.CanDeletePermanently);
    }
}
