using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class AssetWorkflowEndpoints
{
    public static void MapAssetWorkflowEndpoints(this WebApplication app)
    {
        // Viewer floor for the group; service enforces the per-action role gate
        // (author-bound submit, Manager+ for approve/reject/publish/unpublish).
        var group = app.MapGroup("/api/v1/assets/{id:guid}/workflow")
            .RequireAuthorization("RequireViewer")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Asset Workflow");

        group.MapGet("/", Get).WithName("GetAssetWorkflow");

        group.MapPost("submit", Submit)
            .AddEndpointFilter<ValidationFilter<WorkflowActionDto>>()
            .DisableAntiforgery()
            .WithName("SubmitAssetForReview");

        group.MapPost("approve", Approve)
            .AddEndpointFilter<ValidationFilter<WorkflowActionDto>>()
            .DisableAntiforgery()
            .WithName("ApproveAssetReview");

        group.MapPost("reject", Reject)
            .AddEndpointFilter<ValidationFilter<WorkflowRejectDto>>()
            .DisableAntiforgery()
            .WithName("RejectAssetReview");

        group.MapPost("publish", Publish)
            .AddEndpointFilter<ValidationFilter<WorkflowActionDto>>()
            .DisableAntiforgery()
            .WithName("PublishAsset");

        group.MapPost("unpublish", Unpublish)
            .AddEndpointFilter<ValidationFilter<WorkflowActionDto>>()
            .DisableAntiforgery()
            .WithName("UnpublishAsset");
    }

    private static async Task<IResult> Get(
        Guid id,
        [FromServices] IAssetWorkflowService svc,
        CancellationToken ct)
        => (await svc.GetAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> Submit(
        Guid id,
        WorkflowActionDto dto,
        [FromServices] IAssetWorkflowService svc,
        CancellationToken ct)
        => (await svc.SubmitAsync(id, dto, ct)).ToHttpResult();

    private static async Task<IResult> Approve(
        Guid id,
        WorkflowActionDto dto,
        [FromServices] IAssetWorkflowService svc,
        CancellationToken ct)
        => (await svc.ApproveAsync(id, dto, ct)).ToHttpResult();

    private static async Task<IResult> Reject(
        Guid id,
        WorkflowRejectDto dto,
        [FromServices] IAssetWorkflowService svc,
        CancellationToken ct)
        => (await svc.RejectAsync(id, dto, ct)).ToHttpResult();

    private static async Task<IResult> Publish(
        Guid id,
        WorkflowActionDto dto,
        [FromServices] IAssetWorkflowService svc,
        CancellationToken ct)
        => (await svc.PublishAsync(id, dto, ct)).ToHttpResult();

    private static async Task<IResult> Unpublish(
        Guid id,
        WorkflowActionDto dto,
        [FromServices] IAssetWorkflowService svc,
        CancellationToken ct)
        => (await svc.UnpublishAsync(id, dto, ct)).ToHttpResult();
}
