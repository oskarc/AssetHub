using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class AssetCommentEndpoints
{
    public static void MapAssetCommentEndpoints(this WebApplication app)
    {
        // Asset-level group — viewer is the floor. Service enforces
        // contributor-or-higher for create and author-or-admin for delete.
        var group = app.MapGroup("/api/v1/assets/{id:guid}/comments")
            .RequireAuthorization("RequireViewer")
            .WithTags("Asset Comments");

        group.MapGet("/", List).WithName("ListAssetComments");
        group.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<CreateAssetCommentDto>>()
            .DisableAntiforgery()
            .WithName("CreateAssetComment");

        // Single-comment mutations are keyed by commentId; the {id} asset
        // route param comes along for logging / audit context but the
        // service re-resolves authorization per comment.
        group.MapPatch("/{commentId:guid}", Update)
            .AddEndpointFilter<ValidationFilter<UpdateAssetCommentDto>>()
            .DisableAntiforgery()
            .WithName("UpdateAssetComment");
        group.MapDelete("/{commentId:guid}", Delete)
            .DisableAntiforgery()
            .WithName("DeleteAssetComment");
    }

    private static async Task<IResult> List(
        Guid id,
        [FromServices] IAssetCommentService svc,
        CancellationToken ct)
        => (await svc.ListForAssetAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> Create(
        Guid id,
        CreateAssetCommentDto dto,
        [FromServices] IAssetCommentService svc,
        CancellationToken ct)
        => (await svc.CreateAsync(id, dto, ct)).ToHttpResult(
            value => Results.Created($"/api/v1/assets/{id}/comments/{value.Id}", value));

    private static async Task<IResult> Update(
        Guid id,
        Guid commentId,
        UpdateAssetCommentDto dto,
        [FromServices] IAssetCommentService svc,
        CancellationToken ct)
        => (await svc.UpdateAsync(commentId, dto, ct)).ToHttpResult();

    private static async Task<IResult> Delete(
        Guid id,
        Guid commentId,
        [FromServices] IAssetCommentService svc,
        CancellationToken ct)
        => (await svc.DeleteAsync(commentId, ct)).ToHttpResult();
}
