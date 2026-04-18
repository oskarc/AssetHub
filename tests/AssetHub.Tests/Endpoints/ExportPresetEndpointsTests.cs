using System.Net;
using System.Net.Http.Json;
using AssetHub.Application.Dtos;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// Integration tests for ExportPreset admin endpoints (CRUD).
/// </summary>
[Collection("Api")]
public class ExportPresetEndpointsTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ExportPresetEndpointsTests(CustomWebApplicationFactory factory) => _factory = factory;

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
        db.ExportPresets.RemoveRange(db.ExportPresets);
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

    private const string BasePath = "/api/v1/admin/export-presets";

    // ── GET /api/v1/export-presets (viewer-level list endpoint) ──────

    [Fact]
    public async Task GetAll_Admin_Returns200()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/v1/export-presets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_Viewer_Returns200()
    {
        var client = ViewerClient();
        var response = await client.GetAsync("/api/v1/export-presets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── POST /api/v1/admin/export-presets ────────────────────────────

    [Fact]
    public async Task Create_ValidDto_Returns201()
    {
        var client = AdminClient();
        var dto = new CreateExportPresetDto
        {
            Name = $"Preset-{Guid.NewGuid():N}",
            FitMode = "contain",
            Format = "jpeg",
            Quality = 80,
            Width = 1920,
            Height = 1080
        };

        var response = await client.PostAsJsonAsync(BasePath, dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExportPresetDto>();
        Assert.NotNull(body);
        Assert.Equal(dto.Name, body!.Name);
        Assert.Equal("contain", body.FitMode);
        Assert.Equal("jpeg", body.Format);
        Assert.Equal(80, body.Quality);
    }

    [Fact]
    public async Task Create_MissingName_Returns400()
    {
        var client = AdminClient();
        var dto = new { FitMode = "contain", Format = "jpeg" };

        var response = await client.PostAsJsonAsync(BasePath, dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidFitMode_Returns400()
    {
        var client = AdminClient();
        var dto = new CreateExportPresetDto
        {
            Name = $"Preset-{Guid.NewGuid():N}",
            FitMode = "invalid",
            Format = "jpeg"
        };

        var response = await client.PostAsJsonAsync(BasePath, dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var client = AdminClient();
        var name = $"Preset-{Guid.NewGuid():N}";
        var dto = new CreateExportPresetDto
        {
            Name = name,
            FitMode = "contain",
            Format = "jpeg"
        };

        // Create the first one
        var first = await client.PostAsJsonAsync(BasePath, dto);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Try to create duplicate
        var second = await client.PostAsJsonAsync(BasePath, dto);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ── GET /api/v1/admin/export-presets/{id} ────────────────────────

    [Fact]
    public async Task GetById_ExistingPreset_Returns200()
    {
        var client = AdminClient();
        var createDto = new CreateExportPresetDto
        {
            Name = $"Preset-{Guid.NewGuid():N}",
            FitMode = "cover",
            Format = "png",
            Width = 800
        };
        var createResponse = await client.PostAsJsonAsync(BasePath, createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ExportPresetDto>();

        var response = await client.GetAsync($"{BasePath}/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExportPresetDto>();
        Assert.Equal(created.Id, body!.Id);
        Assert.Equal(createDto.Name, body.Name);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.GetAsync($"{BasePath}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── PATCH /api/v1/admin/export-presets/{id} ──────────────────────

    [Fact]
    public async Task Update_ValidDto_Returns200()
    {
        var client = AdminClient();
        var created = await CreatePresetAsync(client, $"Before-{Guid.NewGuid():N}");

        var updateDto = new UpdateExportPresetDto { Name = $"After-{Guid.NewGuid():N}", Quality = 95 };
        var response = await client.PatchAsJsonAsync($"{BasePath}/{created.Id}", updateDto);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ExportPresetDto>();
        Assert.Equal(updateDto.Name, body!.Name);
        Assert.Equal(95, body.Quality);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var client = AdminClient();
        var updateDto = new UpdateExportPresetDto { Name = "irrelevant" };
        var response = await client.PatchAsJsonAsync($"{BasePath}/{Guid.NewGuid()}", updateDto);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DELETE /api/v1/admin/export-presets/{id} ─────────────────────

    [Fact]
    public async Task Delete_ExistingPreset_Returns204()
    {
        var client = AdminClient();
        var created = await CreatePresetAsync(client, $"ToDelete-{Guid.NewGuid():N}");

        var response = await client.DeleteAsync($"{BasePath}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify actually deleted
        var getResponse = await client.GetAsync($"{BasePath}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var client = AdminClient();
        var response = await client.DeleteAsync($"{BasePath}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private async Task<ExportPresetDto> CreatePresetAsync(HttpClient client, string name)
    {
        var dto = new CreateExportPresetDto
        {
            Name = name,
            FitMode = "contain",
            Format = "jpeg",
            Quality = 85,
            Width = 1920,
            Height = 1080
        };
        var response = await client.PostAsJsonAsync(BasePath, dto);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExportPresetDto>())!;
    }
}
