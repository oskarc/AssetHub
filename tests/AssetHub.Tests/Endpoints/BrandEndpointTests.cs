using System.Net;
using System.Net.Http.Json;
using AssetHub.Application.Dtos;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

[Collection("Api")]
public class BrandEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public BrandEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

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
        // Clear collection brand assignments before deleting brands so the
        // SetNull FK doesn't fight with cascading test cleanup.
        var collections = await db.Collections.Where(c => c.BrandId != null).ToListAsync();
        foreach (var c in collections) c.BrandId = null;
        db.Brands.RemoveRange(db.Brands);
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

    [Fact]
    public async Task List_NonAdmin_Returns403()
    {
        var response = await ViewerClient().GetAsync("/api/v1/admin/brands");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateListRoundTrip_Admin_Succeeds()
    {
        var client = AdminClient();
        var dto = new CreateBrandDto
        {
            Name = $"Brand-{Guid.NewGuid():N}",
            PrimaryColor = "#FF0000",
            SecondaryColor = "#00AA00"
        };

        var create = await client.PostAsJsonAsync("/api/v1/admin/brands", dto);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<BrandResponseDto>();
        Assert.NotNull(created);
        Assert.Equal(dto.Name, created!.Name);

        var list = await client.GetAsync("/api/v1/admin/brands");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = await list.Content.ReadFromJsonAsync<List<BrandResponseDto>>();
        Assert.Contains(items!, b => b.Id == created.Id);
    }

    [Fact]
    public async Task Create_BadHexColor_Returns400()
    {
        var client = AdminClient();
        var dto = new CreateBrandDto
        {
            Name = "x",
            PrimaryColor = "not-a-color",
            SecondaryColor = "#000"
        };

        var response = await client.PostAsJsonAsync("/api/v1/admin/brands", dto);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SettingDefault_DemotesPrevious()
    {
        var client = AdminClient();

        var first = await client.PostAsJsonAsync("/api/v1/admin/brands", new CreateBrandDto
        {
            Name = "first", PrimaryColor = "#fff", SecondaryColor = "#000", IsDefault = true
        });
        var firstDto = await first.Content.ReadFromJsonAsync<BrandResponseDto>();
        Assert.True(firstDto!.IsDefault);

        var second = await client.PostAsJsonAsync("/api/v1/admin/brands", new CreateBrandDto
        {
            Name = "second", PrimaryColor = "#fff", SecondaryColor = "#000", IsDefault = true
        });
        var secondDto = await second.Content.ReadFromJsonAsync<BrandResponseDto>();
        Assert.True(secondDto!.IsDefault);

        // Re-fetch the first brand — it should no longer be the default.
        var refresh = await client.GetAsync($"/api/v1/admin/brands/{firstDto.Id}");
        var refreshed = await refresh.Content.ReadFromJsonAsync<BrandResponseDto>();
        Assert.False(refreshed!.IsDefault);
    }
}
