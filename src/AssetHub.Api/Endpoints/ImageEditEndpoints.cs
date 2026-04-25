using AssetHub.Api.Extensions;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

/// <summary>
/// Endpoint for applying image edits with optional preset generation.
/// </summary>
public static class ImageEditEndpoints
{
    public static void MapImageEditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/assets")
            .RequireAuthorization("RequireContributor")
            .RequireAntiforgeryUnlessBearer()
            .WithTags("ImageEditing");

        group.MapPost("/{id:guid}/edit", ApplyEdit)
            .DisableAntiforgery()
            .WithName("ApplyImageEdit");
    }

    private static async Task<IResult> ApplyEdit(
        Guid id,
        [FromForm] ImageEditRequestDto dto,
        IFormFile file,
        [FromServices] IImageEditingService svc,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "A rendered image file is required"
            });

        var allowedTypes = new[] { "image/png", "image/jpeg", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new ApiError
            {
                Code = "BAD_REQUEST",
                Message = "Only PNG, JPEG, and WebP images are accepted"
            });

        var sanitizedFileName = Path.GetFileName(file.FileName);
        using var stream = file.OpenReadStream();
        var result = await svc.ApplyEditAsync(id, dto, stream, sanitizedFileName, file.Length, ct);
        return result.ToHttpResult();
    }
}
