namespace AssetHub.Application.Services.Email.Templates;

/// <summary>
/// Generic per-notification email. Built from an already-persisted
/// <c>Notification</c> + a resolved app link + a Data Protection-signed
/// unsubscribe URL. The subject mirrors the notification title so the user's
/// inbox reads like their in-app bell.
/// </summary>
public sealed class NotificationEmailTemplate : EmailTemplateBase
{
    private readonly string _title;
    private readonly string? _body;
    private readonly string? _deepLinkUrl;
    private readonly string _unsubscribeUrl;
    private readonly string _categoryLabel;

    public NotificationEmailTemplate(
        string title,
        string? body,
        string? deepLinkUrl,
        string unsubscribeUrl,
        string categoryLabel)
    {
        _title = title;
        _body = body;
        _deepLinkUrl = deepLinkUrl;
        _unsubscribeUrl = unsubscribeUrl;
        _categoryLabel = categoryLabel;
    }

    public override string Subject => _title;

    protected override string GetContentHtml()
    {
        var bodyHtml = string.IsNullOrWhiteSpace(_body)
            ? string.Empty
            : $"<p>{EscapeHtml(_body)}</p>";

        var cta = string.IsNullOrWhiteSpace(_deepLinkUrl)
            ? string.Empty
            : $@"<div style=""text-align: center;"">
                    <a href=""{EscapeHtml(_deepLinkUrl)}"" class=""button"">View in AssetHub</a>
                 </div>";

        return $@"
            <h2 style=""margin-top: 0;"">{EscapeHtml(_title)}</h2>
            {bodyHtml}
            {cta}
            <p style=""color: #888; font-size: 12px; margin-top: 32px;"">
                You're receiving this email because <strong>{EscapeHtml(_categoryLabel)}</strong>
                notifications are enabled on your account.
                <a href=""{EscapeHtml(_unsubscribeUrl)}"">Unsubscribe from this category</a>.
            </p>";
    }

    protected override string GetContentPlainText()
    {
        var bodyText = string.IsNullOrWhiteSpace(_body) ? string.Empty : $"\n{_body}\n";
        var cta = string.IsNullOrWhiteSpace(_deepLinkUrl) ? string.Empty : $"\nOpen: {_deepLinkUrl}\n";

        return $@"{_title}
{bodyText}{cta}
---
You're receiving this email because {_categoryLabel} notifications are enabled on your account.
Unsubscribe: {_unsubscribeUrl}";
    }
}
