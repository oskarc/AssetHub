using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// HTTP-level integration tests for the /api/v1/admin/migrations endpoints.
/// </summary>
[Collection("Api")]
public class MigrationEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public MigrationEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

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
        db.MigrationItems.RemoveRange(db.MigrationItems);
        db.Migrations.RemoveRange(db.Migrations);
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());

    private const string BasePath = "/api/v1/admin/migrations";

    private async Task<MigrationResponseDto> CreateMigrationAsync(HttpClient client, string? name = null, bool dryRun = false)
    {
        var dto = new CreateMigrationDto
        {
            Name = name ?? $"Mig-{Guid.NewGuid():N}",
            SourceType = "csv_upload",
            DryRun = dryRun
        };
        var response = await client.PostAsJsonAsync(BasePath, dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MigrationResponseDto>();
        Assert.NotNull(body);
        return body!;
    }

    private static MultipartFormDataContent MakeCsvForm(string csv, string name = "manifest.csv")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { content, "file", name } };
        return form;
    }

    // ── Authorization ────────────────────────────────────────────────

    [Fact]
    public async Task List_Viewer_Returns403()
    {
        var response = await ViewerClient().GetAsync(BasePath);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_Viewer_Returns403()
    {
        var dto = new CreateMigrationDto { Name = "X", SourceType = "csv_upload" };
        var response = await ViewerClient().PostAsJsonAsync(BasePath, dto);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Viewer_Returns403()
    {
        var response = await ViewerClient().GetAsync($"{BasePath}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── List / Get / Create ─────────────────────────────────────────

    [Fact]
    public async Task List_Admin_Returns200AndEmpty()
    {
        var response = await AdminClient().GetAsync(BasePath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MigrationListResponse>();
        Assert.NotNull(body);
        Assert.Empty(body!.Migrations);
    }

    [Fact]
    public async Task Create_Admin_Returns201WithLocation()
    {
        var client = AdminClient();
        var dto = new CreateMigrationDto
        {
            Name = "Integration migration",
            SourceType = "csv_upload",
            DryRun = false
        };

        var response = await client.PostAsJsonAsync(BasePath, dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<MigrationResponseDto>();
        Assert.NotNull(body);
        Assert.Equal("Integration migration", body!.Name);
        Assert.Equal("draft", body.Status);
    }

    [Fact]
    public async Task Create_InvalidSourceType_Returns400()
    {
        var dto = new CreateMigrationDto { Name = "X", SourceType = "bogus" };
        var response = await AdminClient().PostAsJsonAsync(BasePath, dto);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_NonExistent_Returns404()
    {
        var response = await AdminClient().GetAsync($"{BasePath}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Existing_Returns200()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);

        var response = await client.GetAsync($"{BasePath}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MigrationResponseDto>();
        Assert.Equal(created.Id, body!.Id);
    }

    // ── UploadManifest ───────────────────────────────────────────────

    [Fact]
    public async Task UploadManifest_NoFile_Returns400()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);
        var form = new MultipartFormDataContent
        {
            { new StringContent("not-a-file"), "note" }
        };

        var response = await client.PostAsync($"{BasePath}/{created.Id}/manifest", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadManifest_EmptyFile_Returns400()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);

        var response = await client.PostAsync($"{BasePath}/{created.Id}/manifest", MakeCsvForm(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadManifest_ValidCsv_Returns200AndCreatesItems()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);
        var csv = "filename,title\nphoto1.jpg,My Photo 1\nphoto2.jpg,My Photo 2\n";

        var response = await client.PostAsync($"{BasePath}/{created.Id}/manifest", MakeCsvForm(csv));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refreshed = await client.GetFromJsonAsync<MigrationResponseDto>($"{BasePath}/{created.Id}");
        Assert.Equal(2, refreshed!.ItemsTotal);
    }

    [Fact]
    public async Task UploadManifest_NonExistentMigration_Returns404()
    {
        var client = AdminClient();
        var csv = "filename\nphoto.jpg\n";

        var response = await client.PostAsync($"{BasePath}/{Guid.NewGuid()}/manifest", MakeCsvForm(csv));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Start / Cancel / Retry ──────────────────────────────────────

    [Fact]
    public async Task Start_DraftNoItems_Returns400()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);

        var response = await client.PostAsync($"{BasePath}/{created.Id}/start", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Start_WithItems_Returns200AndTransitionsToRunning()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);
        var csv = "filename\nphoto1.jpg\n";
        await client.PostAsync($"{BasePath}/{created.Id}/manifest", MakeCsvForm(csv));

        var response = await client.PostAsync($"{BasePath}/{created.Id}/start", null);

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
        var refreshed = await client.GetFromJsonAsync<MigrationResponseDto>($"{BasePath}/{created.Id}");
        Assert.Equal("running", refreshed!.Status);
    }

    [Fact]
    public async Task Cancel_NotRunning_Returns400()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);

        var response = await client.PostAsync($"{BasePath}/{created.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Retry_NotInFailedState_Returns400()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);

        var response = await client.PostAsync($"{BasePath}/{created.Id}/retry", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Items / Progress ─────────────────────────────────────────────

    [Fact]
    public async Task Items_Returns200AndPaginatedItems()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);
        var csv = "filename\na.jpg\nb.jpg\nc.jpg\n";
        await client.PostAsync($"{BasePath}/{created.Id}/manifest", MakeCsvForm(csv));

        var response = await client.GetAsync($"{BasePath}/{created.Id}/items?skip=0&take=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MigrationItemListResponse>();
        Assert.NotNull(body);
        Assert.Equal(3, body!.TotalCount);
        Assert.Equal(2, body.Items.Count);
    }

    [Fact]
    public async Task Progress_ReturnsCounts()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);
        var csv = "filename\na.jpg\nb.jpg\n";
        await client.PostAsync($"{BasePath}/{created.Id}/manifest", MakeCsvForm(csv));

        var response = await client.GetAsync($"{BasePath}/{created.Id}/progress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MigrationProgressDto>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.ItemsTotal);
    }

    [Fact]
    public async Task OutcomeCsv_ReturnsTextCsvWithHeader()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);
        var csv = "filename\na.jpg\n";
        await client.PostAsync($"{BasePath}/{created.Id}/manifest", MakeCsvForm(csv));

        var response = await client.GetAsync($"{BasePath}/{created.Id}/outcome.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("external_id,filename,status,target_asset_id,error_code,error_message", text);
        Assert.Contains("a.jpg", text);
    }

    // ── Delete / Bulk delete ─────────────────────────────────────────

    [Fact]
    public async Task Delete_Draft_Returns200()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);

        var response = await client.DeleteAsync($"{BasePath}/{created.Id}");

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
        var notFound = await client.GetAsync($"{BasePath}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task BulkDelete_InvalidFilter_Returns400()
    {
        var response = await AdminClient().DeleteAsync($"{BasePath}/bulk?filter=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkDelete_DraftFilter_DeletesOnlyDrafts()
    {
        var client = AdminClient();

        // Create 2 drafts and seed 1 running via DB
        var draft1 = await CreateMigrationAsync(client);
        var draft2 = await CreateMigrationAsync(client);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
            var running = new Migration
            {
                Id = Guid.NewGuid(),
                Name = "Running",
                SourceType = MigrationSourceType.CsvUpload,
                Status = MigrationStatus.Running,
                CreatedByUserId = "other-user",
                CreatedAt = DateTime.UtcNow
            };
            db.Migrations.Add(running);
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"{BasePath}/bulk?filter=draft");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var remaining = await client.GetFromJsonAsync<MigrationListResponse>(BasePath);
        Assert.Single(remaining!.Migrations);
        Assert.Equal("running", remaining.Migrations[0].Status);

        _ = draft1;
        _ = draft2;
    }

    // ── Unstage ─────────────────────────────────────────────────────

    [Fact]
    public async Task Unstage_NotStaged_Returns400()
    {
        var client = AdminClient();
        var created = await CreateMigrationAsync(client);
        var csv = "filename\na.jpg\n";
        await client.PostAsync($"{BasePath}/{created.Id}/manifest", MakeCsvForm(csv));

        var items = await client.GetFromJsonAsync<MigrationItemListResponse>(
            $"{BasePath}/{created.Id}/items");
        var itemId = items!.Items[0].Id;

        var response = await client.DeleteAsync($"{BasePath}/{created.Id}/items/{itemId}/unstage");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
