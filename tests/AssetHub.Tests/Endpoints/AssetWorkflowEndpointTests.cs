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
/// HTTP-level coverage for /api/v1/assets/{id}/workflow. Real Postgres via
/// CustomWebApplicationFactory; exercises auth gates + the happy-path
/// state-machine round trip.
/// </summary>
[Collection("Api")]
public class AssetWorkflowEndpointTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public AssetWorkflowEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

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
        db.AssetWorkflowTransitions.RemoveRange(db.AssetWorkflowTransitions);
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient AnonymousClient()
    {
        TestAuthHandler.ClaimsOverride = null;
        return _factory.CreateClient();
    }

    private async Task<Guid> SeedAssetAsync(AssetWorkflowState state)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();

        var col = TestData.CreateCollection(name: $"WfCol-{Guid.NewGuid():N}", createdByUserId: TestAuthHandler.AdminUserId);
        var asset = TestData.CreateAsset(title: "wf-test", createdByUserId: TestAuthHandler.AdminUserId);
        asset.WorkflowState = state;
        db.Collections.Add(col);
        db.Assets.Add(asset);
        db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id, addedByUserId: TestAuthHandler.AdminUserId));
        db.CollectionAcls.Add(TestData.CreateAcl(col.Id, TestAuthHandler.AdminUserId, AclRole.Admin));
        await db.SaveChangesAsync();
        return asset.Id;
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        var response = await AnonymousClient().GetAsync($"/api/v1/assets/{Guid.NewGuid()}/workflow");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AssetExists_ReturnsWorkflowAndHistory()
    {
        var id = await SeedAssetAsync(AssetWorkflowState.Draft);
        var response = await AdminClient().GetAsync($"/api/v1/assets/{id}/workflow");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AssetWorkflowResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal("draft", dto!.CurrentState);
        Assert.Empty(dto.History);
    }

    [Fact]
    public async Task Submit_FromDraft_TransitionsToInReview()
    {
        var id = await SeedAssetAsync(AssetWorkflowState.Draft);
        var client = AdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/assets/{id}/workflow/submit",
            new WorkflowActionDto { Reason = "Ready for review" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AssetWorkflowResponseDto>();
        Assert.Equal("in_review", dto!.CurrentState);
        Assert.Single(dto.History);
    }

    [Fact]
    public async Task FullLifecycle_Draft_Submit_Approve_Publish_Unpublish()
    {
        var id = await SeedAssetAsync(AssetWorkflowState.Draft);
        var client = AdminClient();

        var submit = await client.PostAsJsonAsync(
            $"/api/v1/assets/{id}/workflow/submit",
            new WorkflowActionDto());
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        var approve = await client.PostAsJsonAsync(
            $"/api/v1/assets/{id}/workflow/approve",
            new WorkflowActionDto { Reason = "LGTM" });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var publish = await client.PostAsJsonAsync(
            $"/api/v1/assets/{id}/workflow/publish",
            new WorkflowActionDto());
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var unpublish = await client.PostAsJsonAsync(
            $"/api/v1/assets/{id}/workflow/unpublish",
            new WorkflowActionDto());
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);

        var final = await unpublish.Content.ReadFromJsonAsync<AssetWorkflowResponseDto>();
        Assert.Equal("approved", final!.CurrentState);
        Assert.Equal(4, final.History.Count);
    }

    [Fact]
    public async Task Reject_MissingReason_Returns400()
    {
        var id = await SeedAssetAsync(AssetWorkflowState.InReview);
        var client = AdminClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/assets/{id}/workflow/reject",
            new WorkflowRejectDto { Reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
