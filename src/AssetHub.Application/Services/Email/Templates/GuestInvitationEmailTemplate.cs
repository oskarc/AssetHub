namespace AssetHub.Application.Services.Email.Templates;

/// <summary>
/// Magic-link invitation email for an external reviewer (T4-GUEST-01).
/// Subject and body call out the inviter (when known) and the review
/// window so the recipient understands why they got the email and how
/// long the link works.
/// </summary>
public sealed class GuestInvitationEmailTemplate : EmailTemplateBase
{
    private readonly string _magicLinkUrl;
    private readonly string? _inviterName;
    private readonly DateTime _expiresAt;
    private readonly int _collectionCount;

    public GuestInvitationEmailTemplate(
        string magicLinkUrl, string? inviterName, DateTime expiresAt, int collectionCount)
    {
        _magicLinkUrl = magicLinkUrl;
        _inviterName = inviterName;
        _expiresAt = expiresAt;
        _collectionCount = collectionCount;
    }

    public override string Subject => "You've been invited to AssetHub";

    protected override string GetContentHtml()
    {
        var greeting = !string.IsNullOrWhiteSpace(_inviterName)
            ? $"<p><strong>{EscapeHtml(_inviterName)}</strong> has invited you to AssetHub as a guest reviewer.</p>"
            : "<p>You've been invited to AssetHub as a guest reviewer.</p>";

        var collectionLine = _collectionCount == 1
            ? "<p>You'll get view access to <strong>1 collection</strong> after you sign in.</p>"
            : $"<p>You'll get view access to <strong>{_collectionCount} collections</strong> after you sign in.</p>";

        return $@"
            <h2 style=""margin-top: 0;"">Welcome to AssetHub 👋</h2>

            {greeting}
            {collectionLine}

            <div style=""text-align: center; margin: 24px 0;"">
                <a href=""{EscapeHtml(_magicLinkUrl)}"" class=""button"">Accept invitation</a>
            </div>

            <p style=""color: #666; font-size: 14px;"">
                This link is valid until <strong>{_expiresAt.ToLocalTime():MMMM d, yyyy 'at' h:mm tt}</strong>.
                After that you'll need to ask the person who invited you for a fresh link.
            </p>

            <p style=""color: #888; font-size: 12px; margin-top: 32px;"">
                If you weren't expecting this email, you can safely ignore it.
            </p>";
    }

    protected override string GetContentPlainText()
    {
        var greeting = !string.IsNullOrWhiteSpace(_inviterName)
            ? $"{_inviterName} has invited you to AssetHub as a guest reviewer."
            : "You've been invited to AssetHub as a guest reviewer.";

        return $@"Welcome to AssetHub

{greeting}

You'll get view access to {_collectionCount} collection(s) after you sign in.

ACCEPT THE INVITATION:
{_magicLinkUrl}

This link is valid until {_expiresAt.ToLocalTime():MMMM d, yyyy 'at' h:mm tt}.
After that you'll need to ask the person who invited you for a fresh link.

If you weren't expecting this email, you can safely ignore it.";
    }
}
