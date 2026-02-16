using AssetHub.Extensions;
using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Endpoints;

public static class ShareEndpoints
{
    public static void MapShareEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/shares")
            .DisableAntiforgery()
            .WithTags("Shares");

        // Public endpoints (no auth required)
        group.MapGet("{token}", GetSharedAsset).WithName("GetSharedAsset").AllowAnonymous();
        group.MapGet("{token}/download", DownloadSharedAsset).WithName("DownloadSharedAsset").AllowAnonymous();
        group.MapGet("{token}/download-all", DownloadAllSharedAssets).WithName("DownloadAllSharedAssets").AllowAnonymous();
        group.MapGet("{token}/preview", PreviewSharedAsset).WithName("PreviewSharedAsset").AllowAnonymous();

        // Protected endpoints
        var authGroup = group.RequireAuthorization();
        authGroup.MapPost("", CreateShare).WithName("CreateShare");
        authGroup.MapDelete("{id}", RevokeShare).WithName("RevokeShare");
        authGroup.MapPut("{id}/password", UpdateSharePassword).WithName("UpdateSharePassword");
    }

    // ── Public endpoints ─────────────────────────────────────────────────────

    private static async Task<IResult> GetSharedAsset(
        string token, string? password,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct,
        int skip = 0, int take = 50)
    {
        var effectivePassword = GetSharePassword(httpContext, password);
        var result = await svc.GetSharedContentAsync(token, effectivePassword, skip, take, ct);
        return HandleShareResult(result);
    }

    private static async Task<IResult> DownloadSharedAsset(
        string token, string? password, Guid? assetId,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectivePassword = GetSharePassword(httpContext, password);
        var result = await svc.GetDownloadUrlAsync(token, effectivePassword, assetId, ct);
        return HandleShareResult(result, url => Results.Redirect(url));
    }

    private static async Task<IResult> DownloadAllSharedAssets(
        string token, string? password,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectivePassword = GetSharePassword(httpContext, password);
        var result = await svc.StreamDownloadAllAsync(token, effectivePassword, httpContext, ct);
        if (!result.IsSuccess)
            return HandleShareResult(result);
        return Results.Empty;
    }

    private static async Task<IResult> PreviewSharedAsset(
        string token, string? password, string? size, Guid? assetId,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectivePassword = GetSharePassword(httpContext, password);
        var result = await svc.GetPreviewUrlAsync(token, effectivePassword, size, assetId, ct);
        return HandleShareResult(result, url => Results.Redirect(url));
    }

    // ── Protected endpoints ──────────────────────────────────────────────────

    private static async Task<IResult> CreateShare(
        CreateShareDto dto,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var result = await svc.CreateShareAsync(dto, baseUrl, ct);
        return result.ToHttpResult(v => Results.Created($"/api/shares/{v.Id}", v));
    }

    private static async Task<IResult> RevokeShare(
        Guid id, [FromServices] IShareAccessService svc, CancellationToken ct)
    {
        var result = await svc.RevokeShareAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdateSharePassword(
        Guid id, [FromBody] UpdateSharePasswordDto dto,
        [FromServices] IShareAccessService svc, CancellationToken ct)
    {
        var result = await svc.UpdateSharePasswordAsync(id, dto.Password, ct);
        return result.ToHttpResult();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts share password from X-Share-Password header first, then query string fallback.
    /// Header is preferred to avoid passwords in server logs and browser history.
    /// </summary>
    private static string? GetSharePassword(HttpContext httpContext, string? queryPassword)
    {
        var headerPassword = httpContext.Request.Headers["X-Share-Password"].FirstOrDefault();
        return headerPassword ?? queryPassword;
    }

    /// <summary>
    /// Handles ServiceResult for share endpoints, with special handling for
    /// PASSWORD_REQUIRED (401) responses that need a specific body format.
    /// </summary>
    private static IResult HandleShareResult<T>(ServiceResult<T> result, Func<T, IResult>? onSuccess = null)
    {
        if (result.IsSuccess)
            return onSuccess != null ? onSuccess(result.Value!) : Results.Ok(result.Value);

        return result.Error!.Code switch
        {
            "PASSWORD_REQUIRED" => Results.Json(
                new PasswordRequiredResponse { RequiresPassword = true }, statusCode: 401),
            "UNAUTHORIZED" => Results.Unauthorized(),
            _ => result.ToHttpResult()
        };
    }

    private static IResult HandleShareResult(ServiceResult result)
    {
        if (result.IsSuccess)
            return Results.NoContent();

        return result.Error!.Code switch
        {
            "PASSWORD_REQUIRED" => Results.Json(
                new PasswordRequiredResponse { RequiresPassword = true }, statusCode: 401),
            "UNAUTHORIZED" => Results.Unauthorized(),
            _ => result.ToHttpResult()
        };
    }
}
