using AssetHub.Api.Authentication;
using AssetHub.Api.Extensions;
using AssetHub.Api.OpenApi;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services.Watermarking;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

/// <summary>
/// Forensic watermarking API surface (T5-WMK-01) — admin verify endpoint plus
/// the three PATCH endpoints that toggle watermarking at each level of the
/// inheritance chain (Collection → Asset → Share).
///
/// Co-locating all watermark-config endpoints here (rather than splitting across
/// CollectionEndpoints / AssetEndpoints / ShareEndpoints) keeps the watermark
/// surface visible in one place — natural for a feature whose mental model is
/// "watermarks live above the entity tree."
/// </summary>
public static class WatermarkEndpoints
{
    public static void MapWatermarkEndpoints(this WebApplication app)
    {
        MapAdminVerify(app);
        MapCollectionWatermark(app);
        MapAssetWatermark(app);
        MapShareWatermark(app);
    }

    private static void MapAdminVerify(WebApplication app)
    {
        var adminGroup = app.MapGroup("/api/v1/admin/watermarks")
            .RequireAuthorization("RequireAdmin")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Watermarks");

        var adminScope = new RequireScopeFilter("admin");

        adminGroup.MapPost("/verify", async (
            [FromForm] IFormFile file,
            [FromServices] IWatermarkVerifier verifier,
            CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(ApiError.BadRequest("File is required."));
                }

                await using var stream = file.OpenReadStream();
                var result = await verifier.VerifyAsync(
                    stream, file.ContentType ?? string.Empty, file.Length, ct);
                return result.ToHttpResult();
            })
            .MarkAsPublicMutation(adminScope)
            .WithSummary("Verify a leaked watermarked image and return attribution.");
    }

    private static void MapCollectionWatermark(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/collections/{id:guid}/watermark")
            .RequireAuthorization("RequireManager")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Watermarks");

        var write = new RequireScopeFilter("collections:write");

        group.MapPatch("/", async (
            Guid id,
            UpdateCollectionWatermarkDto dto,
            [FromServices] IWatermarkService svc,
            CancellationToken ct) =>
            (await svc.SetCollectionWatermarkAsync(id, dto.Enabled, ct)).ToHttpResult())
            .MarkAsPublicMutation(write)
            .WithSummary("Toggle forensic watermarking on a collection.");
    }

    private static void MapAssetWatermark(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets/{id:guid}/watermark")
            .RequireAuthorization("RequireContributor")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Watermarks");

        var write = new RequireScopeFilter("assets:write");

        group.MapPatch("/", async (
            Guid id,
            UpdateAssetWatermarkDto dto,
            [FromServices] IWatermarkService svc,
            CancellationToken ct) =>
            (await svc.SetAssetWatermarkOverrideAsync(id, dto.Override, ct)).ToHttpResult())
            .MarkAsPublicMutation(write)
            .WithSummary("Override or clear forensic watermarking for a single asset.");
    }

    private static void MapShareWatermark(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/shares/{id:guid}/watermark")
            .RequireAuthorization("RequireContributor")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Watermarks");

        var write = new RequireScopeFilter("shares:write");

        group.MapPatch("/", async (
            Guid id,
            UpdateShareWatermarkDto dto,
            [FromServices] IWatermarkService svc,
            CancellationToken ct) =>
            (await svc.SetShareWatermarkOverrideAsync(id, dto.Override, ct)).ToHttpResult())
            .MarkAsPublicMutation(write)
            .WithSummary("Override or clear forensic watermarking for a single share.");
    }
}
