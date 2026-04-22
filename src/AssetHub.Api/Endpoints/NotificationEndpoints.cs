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
}
