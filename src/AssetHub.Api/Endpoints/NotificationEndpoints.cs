using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AssetHub.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/notifications")
            // Any authenticated user can read / mutate their own notifications.
            // The service layer scopes every call to CurrentUser.UserId.
            .RequireAuthorization()
            .RequireAntiforgeryUnlessBearer()
            .WithTags("Notifications");

        group.MapGet("", List).WithName("ListNotifications");
        group.MapGet("unread-count", GetUnreadCount).WithName("GetNotificationUnreadCount");
        group.MapPost("{id:guid}/read", MarkRead)
            .DisableAntiforgery()
            .WithName("MarkNotificationRead");
        group.MapPost("read-all", MarkAllRead)
            .DisableAntiforgery()
            .WithName("MarkAllNotificationsRead");
        group.MapDelete("{id:guid}", Delete)
            .DisableAntiforgery()
            .WithName("DeleteNotification");

        // Preferences
        var prefsGroup = group.MapGroup("preferences");
        prefsGroup.MapGet("", GetPreferences).WithName("GetNotificationPreferences");
        prefsGroup.MapPut("", UpdatePreferences)
            .AddEndpointFilter<ValidationFilter<UpdateNotificationPreferencesDto>>()
            .DisableAntiforgery()
            .WithName("UpdateNotificationPreferences");
        prefsGroup.MapPost("rotate-unsubscribe-token", RotateUnsubscribeToken)
            .DisableAntiforgery()
            .WithName("RotateNotificationUnsubscribeToken");

        // Anonymous one-click unsubscribe from email links. The token is a
        // signed payload (userId, category, stamp) — the endpoint never
        // accepts a user id directly. Returns a minimal HTML confirmation so
        // a browser click lands on a readable page. Shares the anonymous
        // rate-limit policy with public share access — same risk profile
        // (anonymous + signed token) so log-flood / CPU-drain via invalid
        // tokens is capped.
        group.MapGet("unsubscribe", Unsubscribe)
            .AllowAnonymous()
            .RequireRateLimiting(Constants.RateLimitPolicies.ShareAnonymous)
            .WithName("NotificationUnsubscribe");
    }

    private static async Task<IResult> List(
        [FromServices] INotificationService svc,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        return (await svc.ListForCurrentUserAsync(unreadOnly, skip, take, ct)).ToHttpResult();
    }

    private static async Task<IResult> GetUnreadCount(
        [FromServices] INotificationService svc, CancellationToken ct)
    {
        return (await svc.GetUnreadCountForCurrentUserAsync(ct)).ToHttpResult();
    }

    private static async Task<IResult> MarkRead(
        Guid id, [FromServices] INotificationService svc, CancellationToken ct)
    {
        return (await svc.MarkReadAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> MarkAllRead(
        [FromServices] INotificationService svc, CancellationToken ct)
    {
        return (await svc.MarkAllReadForCurrentUserAsync(ct)).ToHttpResult();
    }

    private static async Task<IResult> Delete(
        Guid id, [FromServices] INotificationService svc, CancellationToken ct)
    {
        return (await svc.DeleteAsync(id, ct)).ToHttpResult();
    }

    private static async Task<IResult> GetPreferences(
        [FromServices] INotificationPreferencesService svc, CancellationToken ct)
    {
        return (await svc.GetForCurrentUserAsync(ct)).ToHttpResult();
    }

    private static async Task<IResult> UpdatePreferences(
        UpdateNotificationPreferencesDto dto,
        [FromServices] INotificationPreferencesService svc,
        CancellationToken ct)
    {
        return (await svc.UpdateForCurrentUserAsync(dto, ct)).ToHttpResult();
    }

    private static async Task<IResult> RotateUnsubscribeToken(
        [FromServices] INotificationPreferencesService svc,
        CancellationToken ct)
    {
        return (await svc.RotateUnsubscribeTokenAsync(ct)).ToHttpResult();
    }

    private static async Task<IResult> Unsubscribe(
        [FromQuery] string? token,
        [FromServices] INotificationPreferencesService svc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Results.Content(UnsubscribeHtml(applied: false, category: null),
                "text/html; charset=utf-8", statusCode: 400);

        var result = await svc.UnsubscribeFromCategoryAsync(token, ct);
        if (!result.IsSuccess || result.Value is null)
            return Results.Content(UnsubscribeHtml(applied: false, category: null),
                "text/html; charset=utf-8", statusCode: 400);

        return Results.Content(
            UnsubscribeHtml(result.Value.Applied, result.Value.Category),
            "text/html; charset=utf-8");
    }

    private static string UnsubscribeHtml(bool applied, string? category)
    {
        // Intentionally plain HTML — this page is hit from an email client
        // outside the Blazor Server session, so we don't route through the
        // Ui layout. Localised strings would require the request to carry a
        // culture; keep it English for now and revisit if an email localiser
        // lands.
        var title = applied ? "Unsubscribed" : "Unsubscribe link not valid";
        var message = applied
            ? $"You've been unsubscribed from <strong>{System.Net.WebUtility.HtmlEncode(category ?? string.Empty)}</strong> emails. You can re-enable this category from Account → Notification preferences."
            : "This unsubscribe link is invalid or has expired. You can manage every notification category from Account → Notification preferences.";
        const string style =
            "body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f5f5f5;margin:0;padding:48px 16px;color:#333}"
            + ".card{max-width:560px;margin:0 auto;background:#fff;border-radius:8px;padding:32px;box-shadow:0 2px 8px rgba(0,0,0,.08)}"
            + "h1{margin-top:0;color:#1976D2}"
            + "p{line-height:1.6}";
        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + $"<title>{title} — AssetHub</title>"
            + $"<style>{style}</style></head><body>"
            + $"<div class=\"card\"><h1>{title}</h1><p>{message}</p></div>"
            + "</body></html>";
    }
}
