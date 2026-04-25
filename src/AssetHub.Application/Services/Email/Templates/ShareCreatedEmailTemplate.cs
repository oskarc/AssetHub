namespace AssetHub.Application.Services.Email.Templates;

/// <summary>
/// Email template sent when a new share link is created.
/// Contains the share URL and password for accessing the shared content.
/// </summary>
public class ShareCreatedEmailTemplate : EmailTemplateBase
{
    private readonly string _shareUrl;
    private readonly string _password;
    private readonly string _contentName;
    private readonly string _contentType; // "asset" or "collection"
    private readonly string? _senderName;
    private readonly DateTime? _expiresAt;

    public ShareCreatedEmailTemplate(
        string shareUrl,
        string password,
        string contentName,
        string contentType,
        string? senderName = null,
        DateTime? expiresAt = null)
    {
        _shareUrl = shareUrl;
        _password = password;
        _contentName = contentName;
        _contentType = contentType;
        _senderName = senderName;
        _expiresAt = expiresAt;
    }

    public override string Subject => $"You've been invited to view shared content on AssetHub";

    protected override string GetContentHtml()
    {
        var article = StartsWithVowelSound(_contentType) ? "an" : "a";
        var greeting = !string.IsNullOrEmpty(_senderName)
            ? $"<p><strong>{EscapeHtml(_senderName)}</strong> has shared {article} {_contentType} with you!</p>"
            : $"<p>Someone has shared {article} {_contentType} with you!</p>";

        var expiryInfo = _expiresAt.HasValue
            ? $@"<p style=""color: #666; font-size: 14px;"">
                    <strong>Note:</strong> This link will expire on {_expiresAt.Value.ToLocalTime():MMMM d, yyyy 'at' h:mm tt}.
                </p>"
            : "";

        return $@"
            <h2 style=""margin-top: 0;"">Welcome! 👋</h2>
            
            {greeting}
            
            <p>You've been granted access to: <strong>{EscapeHtml(_contentName)}</strong></p>
            
            <div class=""info-box"">
                <div class=""info-box-label"">Share Link</div>
                <div class=""info-box-value"">{EscapeHtml(_shareUrl)}</div>
            </div>
            
            <div class=""password-box"">
                <div class=""info-box-label"">Password</div>
                <div class=""info-box-value"" style=""font-size: 18px; font-weight: bold;"">{EscapeHtml(_password)}</div>
                <p class=""warning-text"">⚠️ Keep this password secure. You'll need it to access the shared content.</p>
            </div>
            
            <div style=""text-align: center;"">
                <a href=""{EscapeHtml(_shareUrl)}"" class=""button"">View Shared Content</a>
            </div>
            
            {expiryInfo}
            
            <p style=""color: #666; font-size: 14px; margin-top: 24px;"">
                If you weren't expecting this email, you can safely ignore it.
            </p>";
    }

    protected override string GetContentPlainText()
    {
        var article = StartsWithVowelSound(_contentType) ? "an" : "a";
        var greeting = !string.IsNullOrEmpty(_senderName)
            ? $"{_senderName} has shared {article} {_contentType} with you!"
            : $"Someone has shared {article} {_contentType} with you!";

        var expiryInfo = _expiresAt.HasValue
            ? $"\nNote: This link will expire on {_expiresAt.Value.ToLocalTime():MMMM d, yyyy 'at' h:mm tt}."
            : "";

        return $@"Welcome!

{greeting}

You've been granted access to: {_contentName}

SHARE LINK:
{_shareUrl}

PASSWORD:
{_password}

Keep this password secure. You'll need it to access the shared content.
{expiryInfo}

If you weren't expecting this email, you can safely ignore it.";
    }

    private static readonly HashSet<char> VowelSoundStarts = new() { 'a', 'e', 'i', 'o', 'u' };

    private static bool StartsWithVowelSound(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        return VowelSoundStarts.Contains(char.ToLowerInvariant(word[0]));
    }
}
