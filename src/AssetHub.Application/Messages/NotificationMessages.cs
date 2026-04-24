namespace AssetHub.Application.Messages;

// ── Commands ─────────────────────────────────────────────────────────────

/// <summary>
/// Sends the email for an already-persisted in-app notification. Published by
/// <c>NotificationService.CreateAsync</c> when the recipient's preferences
/// resolve to <c>Email = true</c> and <c>EmailCadence = instant</c> for that
/// category. The handler loads the notification, resolves the recipient's
/// email via <c>IUserLookupService</c>, and sends via <c>IEmailService</c>.
///
/// Wolverine retry policy (configured in Worker Program.cs) handles transient
/// SMTP failures; permanent failures are logged and dropped so one broken
/// recipient can't jam the queue.
/// </summary>
public record SendNotificationEmailCommand
{
    public Guid NotificationId { get; init; }
}
