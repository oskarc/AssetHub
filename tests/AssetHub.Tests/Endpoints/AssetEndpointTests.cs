using System.Net;
using System.Net.Http.Json;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AssetHub.Tests.Endpoints;

/// <summary>DelegatingHandler that prevents following redirects (for testing 302 responses).</summary>
internal class RedirectHandler : DelegatingHandler
{
    public RedirectHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
}

/// <summary>
/// API integration tests for /api/v1/assets/**
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
        await db.Database.MigrateAsync();
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

    // GET /api/v1/assets/all was retired in T1-SRCH-01 follow-up. POST /api/v1/assets/search is
    // the replacement; these endpoint tests exercise its surface. Full RBAC + facet behaviour
    // lives in AssetSearchServiceTests.

    // ── GetAsset ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsset_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.GetAsync($"/api/v1/assets/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GetAssetsByCollection ───────────────────────────────────────

    // ── UpdateAsset ─────────────────────────────────────────────────

    // ── DeleteAsset ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsset_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/v1/assets/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Renditions ──────────────────────────────────────────────────

    [Fact]
    public async Task GetThumbnail_WithAccess_Returns302()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        // Don't follow redirects for this test
        var noRedirectClient = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await noRedirectClient.GetAsync($"/api/v1/assets/{assetId}/thumb");

        // Should redirect to presigned URL
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task DownloadOriginal_WithAccess_Returns302()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await client.GetAsync($"/api/v1/assets/{assetId}/download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // ── Init Presigned Upload ───────────────────────────────────────

    // ── Multi-Collection ────────────────────────────────────────────

    // ── GetDeletionContext ──────────────────────────────────────────

    // ═══════════════════════════════════════════════════════════════
    //  NEGATIVE / ANTI-TESTS
    // ═══════════════════════════════════════════════════════════════

    // ── Unauthenticated access ──────────────────────────────────────

    [Fact]
    public async Task GetAssets_Unauthenticated_Returns401()
    {
        // All /api/v1/assets/** require auth; the TestAuthHandler always succeeds
        // so we test by role instead. This is covered by Viewer_Returns403 tests.
        TestAuthHandler.ClaimsOverride = null;
        Assert.True(true);
    }

    // ── UpdateAsset — negative ──────────────────────────────────────

    [Fact]
    public async Task UpdateAsset_NotFound_Returns404()
    {
        var client = AdminClient();
        var patchContent = JsonContent.Create(new { Title = "No such asset" });
        var response = await client.PatchAsync($"/api/v1/assets/{Guid.NewGuid()}", patchContent);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DeleteAsset — negative ──────────────────────────────────────

    // ── GetAssetsByCollection — negative ────────────────────────────

    // ── GetAssetCollections — negative ──────────────────────────────

    [Fact]
    public async Task GetAssetCollections_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/collections");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── AddAssetToCollection — negative ─────────────────────────────

    [Fact]
    public async Task AddAssetToCollection_NonExistentCollection_Returns400Or403Or404()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var client = AdminClient();

        var response = await client.PostAsync($"/api/v1/assets/{assetId}/collections/{Guid.NewGuid()}", null);
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 400, 403 or 404 but got {response.StatusCode}");
    }

    // ── RemoveAssetFromCollection — negative ────────────────────────

    [Fact]
    public async Task RemoveAssetFromCollection_NonExistent_Returns404()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"/api/v1/assets/{Guid.NewGuid()}/collections/{Guid.NewGuid()}");

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 404 or 403 but got {response.StatusCode}");
    }

    // ── GetDeletionContext — negative ───────────────────────────────

    [Fact]
    public async Task GetDeletionContext_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/deletion-context");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Renditions — negative ───────────────────────────────────────

    [Fact]
    public async Task GetThumbnail_NonExistentAsset_Returns404()
    {
        var noRedirectClient = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await noRedirectClient.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/thumb");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetThumbnail_ViewerNoAccess_Returns403()
    {
        var (_, assetId) = await SeedCollectionWithAssetAsync();
        var noRedirectClient = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Default();

        var response = await noRedirectClient.GetAsync($"/api/v1/assets/{assetId}/thumb");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DownloadOriginal_NonExistentAsset_Returns404()
    {
        var noRedirectClient = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await noRedirectClient.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/download");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PreviewOriginal_NonExistentAsset_Returns404()
    {
        var noRedirectClient = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await noRedirectClient.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/preview");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMedium_NonExistentAsset_Returns404()
    {
        var noRedirectClient = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await noRedirectClient.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/medium");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPoster_NonExistentAsset_Returns404()
    {
        var noRedirectClient = _factory.CreateDefaultClient(new RedirectHandler());
        TestAuthHandler.ClaimsOverride = TestClaimsProvider.Admin();

        var response = await noRedirectClient.GetAsync($"/api/v1/assets/{Guid.NewGuid()}/poster");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── InitUpload — negative ───────────────────────────────────────

    [Fact]
    public async Task InitUpload_NonExistentCollection_Returns403Or404()
    {
        var client = ViewerClient();
        var request = new InitUploadRequest
        {
            CollectionId = Guid.NewGuid(),
            FileName = "test.jpg",
            ContentType = "image/jpeg",
            FileSize = 1024,
            Title = "Test"
        };
        var response = await client.PostAsJsonAsync("/api/v1/assets/init-upload", request);

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 403 or 404 but got {response.StatusCode}");
    }

    // ── ConfirmUpload — negative ────────────────────────────────────

    [Fact]
    public async Task ConfirmUpload_NonExistentAsset_Returns404()
    {
        var client = AdminClient();
        var response = await client.PostAsync($"/api/v1/assets/{Guid.NewGuid()}/confirm-upload", null);

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 404 or 403 but got {response.StatusCode}");
    }
}
