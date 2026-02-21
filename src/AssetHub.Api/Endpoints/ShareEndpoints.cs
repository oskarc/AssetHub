using AssetHub.Api.Extensions;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

public static class ShareEndpoints
{
    public static void MapShareEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/shares")
            .WithTags("Shares");

        // Public endpoints (no auth required, rate-limited).
        // POST endpoints use .DisableAntiforgery() because they are called by
        // non-browser clients and share link consumers that cannot provide
        // antiforgery tokens. GET endpoints are exempt from antiforgery by default.
        group.MapGet("{token}", GetSharedAsset).WithName("GetSharedAsset")
            .AllowAnonymous().RequireRateLimiting("ShareAnonymous");
        group.MapPost("{token}/access-token", CreateAccessToken).WithName("CreateAccessToken")
            .AllowAnonymous().RequireRateLimiting("SharePassword").DisableAntiforgery();
        group.MapGet("{token}/download", DownloadSharedAsset).WithName("DownloadSharedAsset")
            .AllowAnonymous().RequireRateLimiting("ShareAnonymous");
        group.MapPost("{token}/download-all", DownloadAllSharedAssets).WithName("DownloadAllSharedAssets")
            .AllowAnonymous().RequireRateLimiting("ShareAnonymous").DisableAntiforgery();
        group.MapGet("{token}/preview", PreviewSharedAsset).WithName("PreviewSharedAsset")
            .AllowAnonymous().RequireRateLimiting("ShareAnonymous");

        // Protected endpoints
        var authGroup = group.RequireAuthorization();
        authGroup.MapPost("", CreateShare).WithName("CreateShare");
        authGroup.MapDelete("{id:guid}", RevokeShare).WithName("RevokeShare");
        authGroup.MapPut("{id:guid}/password", UpdateSharePassword).WithName("UpdateSharePassword");
    }

    // ── Public endpoints ─────────────────────────────────────────────────────

    private static async Task<IResult> GetSharedAsset(
        string token,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct,
        int skip = 0, int take = 50)
    {
        take = Math.Clamp(take, 1, Constants.Limits.MaxPageSize);
        var effectivePassword = GetSharePassword(httpContext);
        var result = await svc.GetSharedContentAsync(token, effectivePassword, skip, take, ct);
        return HandleShareResult(result);
    }

    private static async Task<IResult> CreateAccessToken(
        string token,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var password = GetSharePassword(httpContext);
        var result = await svc.CreateAccessTokenAsync(token, password, ct);
        return HandleShareResult(result);
    }

    private static async Task<IResult> DownloadSharedAsset(
        string token, Guid? assetId, string? accessToken,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectiveCredential = GetSharePassword(httpContext) ?? accessToken;
        var result = await svc.GetDownloadUrlAsync(token, effectiveCredential, assetId, ct);
        return HandleShareResult(result, url => Results.Redirect(url));
    }

    private static async Task<IResult> DownloadAllSharedAssets(
        string token, string? accessToken,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectiveCredential = GetSharePassword(httpContext) ?? accessToken;
        var result = await svc.EnqueueDownloadAllAsync(token, effectiveCredential, ct);
        if (!result.IsSuccess)
            return HandleShareResult(result);
        return Results.Accepted(result.Value!.StatusUrl, result.Value);
    }

    private static async Task<IResult> PreviewSharedAsset(
        string token, string? accessToken, string? size, Guid? assetId,
        [FromServices] IShareAccessService svc,
        HttpContext httpContext, CancellationToken ct)
    {
        var effectiveCredential = GetSharePassword(httpContext) ?? accessToken;
        var result = await svc.GetPreviewUrlAsync(token, effectiveCredential, size, assetId, ct);
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
    /// Extracts share password from X-Share-Password header.
    /// Passwords are only accepted via header to avoid leakage in logs,
    /// browser history, and referrer headers. For HTML element attributes
    /// (img src, video src, a href) use short-lived access tokens instead.
    /// </summary>
    private static string? GetSharePassword(HttpContext httpContext)
    {
        return httpContext.Request.Headers["X-Share-Password"].FirstOrDefault();
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
