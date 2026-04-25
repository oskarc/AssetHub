using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        // Admin-only group; service double-checks IsSystemAdmin so this
        // can't accidentally widen if the policy is loosened later.
        var group = app.MapGroup("/api/v1/admin/webhooks")
            .RequireAuthorization("RequireAdmin")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Webhooks");

        group.MapGet("/", List).WithName("ListWebhooks");
        group.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<CreateWebhookDto>>()
            .DisableAntiforgery()
            .WithName("CreateWebhook");

        group.MapGet("{id:guid}", Get).WithName("GetWebhook");
        group.MapPatch("{id:guid}", Update)
            .AddEndpointFilter<ValidationFilter<UpdateWebhookDto>>()
            .DisableAntiforgery()
            .WithName("UpdateWebhook");
        group.MapDelete("{id:guid}", Delete)
            .DisableAntiforgery()
            .WithName("DeleteWebhook");

        group.MapPost("{id:guid}/rotate-secret", RotateSecret)
            .DisableAntiforgery()
            .WithName("RotateWebhookSecret");
        group.MapPost("{id:guid}/test", SendTest)
            .DisableAntiforgery()
            .WithName("SendWebhookTest");
        group.MapGet("{id:guid}/deliveries", ListDeliveries)
            .WithName("ListWebhookDeliveries");
    }

    private static async Task<IResult> List(
        [FromServices] IWebhookService svc, CancellationToken ct)
        => (await svc.ListAsync(ct)).ToHttpResult();

    private static async Task<IResult> Get(
        Guid id, [FromServices] IWebhookService svc, CancellationToken ct)
        => (await svc.GetByIdAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> Create(
        CreateWebhookDto dto, [FromServices] IWebhookService svc, CancellationToken ct)
        => (await svc.CreateAsync(dto, ct)).ToHttpResult(
            value => Results.Created($"/api/v1/admin/webhooks/{value.Webhook.Id}", value));

    private static async Task<IResult> Update(
        Guid id, UpdateWebhookDto dto, [FromServices] IWebhookService svc, CancellationToken ct)
        => (await svc.UpdateAsync(id, dto, ct)).ToHttpResult();

    private static async Task<IResult> Delete(
        Guid id, [FromServices] IWebhookService svc, CancellationToken ct)
        => (await svc.DeleteAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> RotateSecret(
        Guid id, [FromServices] IWebhookService svc, CancellationToken ct)
        => (await svc.RotateSecretAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> SendTest(
        Guid id, [FromServices] IWebhookService svc, CancellationToken ct)
        => (await svc.SendTestAsync(id, ct)).ToHttpResult();

    private static async Task<IResult> ListDeliveries(
        Guid id,
        [FromServices] IWebhookService svc,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
        => (await svc.ListDeliveriesAsync(id, take, ct)).ToHttpResult();
}
