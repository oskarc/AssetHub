using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Worker.Handlers;

/// <summary>
/// Handles <see cref="SendNotificationEmailCommand"/>. Loads the notification,
/// resolves the recipient's email, builds a signed unsubscribe URL, and hands
/// off to <see cref="IEmailService"/>. Silent no-ops when the notification is
/// missing (race with delete), the recipient has no email on file, or SMTP is
/// disabled — Wolverine's retry policy handles transient SMTP failures.
/// </summary>
public sealed class SendNotificationEmailHandler(
    INotificationRepository notificationRepo,
    INotificationPreferencesService preferencesService,
    INotificationUnsubscribeTokenService tokens,
    IUserLookupService userLookup,
    IEmailService emailService,
    IOptions<AppSettings> appSettings,
    ILogger<SendNotificationEmailHandler> logger)
{
    public async Task HandleAsync(SendNotificationEmailCommand command, CancellationToken ct)
    {
        var notification = await notificationRepo.GetByIdAsync(command.NotificationId, ct);
        if (notification is null)
        {
            logger.LogDebug(
                "SendNotificationEmail skipped — notification {NotificationId} no longer exists",
                command.NotificationId);
            return;
        }

        var emails = await userLookup.GetUserEmailsAsync(new[] { notification.UserId }, ct);
        if (!emails.TryGetValue(notification.UserId, out var recipientEmail) ||
            string.IsNullOrWhiteSpace(recipientEmail))
        {
            logger.LogWarning(
                "SendNotificationEmail skipped — no email on file for user {UserId} (notification {NotificationId})",
                notification.UserId, notification.Id);
            return;
        }

        var prefs = await preferencesService.GetByUserIdAsync(notification.UserId, ct);
        if (prefs is null || string.IsNullOrWhiteSpace(prefs.UnsubscribeTokenHash))
        {
            // Defensive — a notification implies prefs exist (CreateAsync calls
            // ResolveForUserAsync). If they don't, skip rather than send an
            // email without a working unsubscribe link.
            logger.LogWarning(
                "SendNotificationEmail skipped — preferences row missing for user {UserId}",
                notification.UserId);
            return;
        }

        var baseUrl = appSettings.Value.BaseUrl?.TrimEnd('/') ?? string.Empty;
        var token = tokens.CreateToken(notification.UserId, notification.Category, prefs.UnsubscribeTokenHash);
        var unsubscribeUrl = $"{baseUrl}/api/v1/notifications/unsubscribe?token={Uri.EscapeDataString(token)}";

        var deepLinkUrl = BuildDeepLinkUrl(notification.Url, baseUrl);

        var template = new NotificationEmailTemplate(
            title: notification.Title,
            body: notification.Body,
            deepLinkUrl: deepLinkUrl,
            unsubscribeUrl: unsubscribeUrl,
            categoryLabel: notification.Category);

        await emailService.SendEmailAsync(recipientEmail, template, ct);

        logger.LogInformation(
            "Notification email sent for {NotificationId} (user {UserId}, category {Category})",
            notification.Id, notification.UserId, notification.Category);
    }

    private static string? BuildDeepLinkUrl(string? relativeUrl, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl) || string.IsNullOrWhiteSpace(baseUrl))
            return null;
        return relativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? relativeUrl
            : $"{baseUrl}/{relativeUrl.TrimStart('/')}";
    }
}
