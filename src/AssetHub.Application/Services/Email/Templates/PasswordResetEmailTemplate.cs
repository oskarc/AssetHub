namespace AssetHub.Application.Services.Email.Templates;

/// <summary>
/// Password-reset email carrying a single-use, expiring link.
/// </summary>
/// <remarks>
/// Sent both for admin-triggered resets and for new-account password setup, so
/// the copy avoids implying the recipient asked for it. The link is the only
/// secret here — it is never logged, and the template deliberately carries no
/// password.
/// </remarks>
public class PasswordResetEmailTemplate : EmailTemplateBase
{
    private readonly string _userName;
    private readonly string _resetUrl;
    private readonly int _validHours;
    private readonly bool _isNewAccount;

    public PasswordResetEmailTemplate(
        string userName,
        string resetUrl,
        int validHours = 24,
        bool isNewAccount = false)
    {
        _userName = userName;
        _resetUrl = resetUrl;
        _validHours = validHours;
        _isNewAccount = isNewAccount;
    }

    public override string Subject => _isNewAccount
        ? "Set your AssetHub password"
        : "Reset your AssetHub password";

    protected override string GetContentHtml()
    {
        var lead = _isNewAccount
            ? "An AssetHub account has been created for you. Choose a password to finish setting it up."
            : "A password reset was requested for your AssetHub account.";

        return $@"
            <p>Hello {System.Net.WebUtility.HtmlEncode(_userName)},</p>
            <p>{lead}</p>
            <p style=""margin: 28px 0;"">
                <a href=""{System.Net.WebUtility.HtmlEncode(_resetUrl)}""
                   style=""background: #1976d2; color: #fff; padding: 12px 22px;
                          border-radius: 4px; text-decoration: none; font-weight: 600;"">
                    {(_isNewAccount ? "Set password" : "Reset password")}
                </a>
            </p>
            <p style=""color: #666; font-size: 14px;"">
                This link can be used once and expires in {_validHours} hours.
            </p>
            <p style=""color: #666; font-size: 14px;"">
                If you weren't expecting this email you can ignore it — your password
                stays unchanged until the link is used.
            </p>";
    }

    protected override string GetContentPlainText()
    {
        var lead = _isNewAccount
            ? "An AssetHub account has been created for you. Choose a password to finish setting it up."
            : "A password reset was requested for your AssetHub account.";

        return $@"Hello {_userName},

{lead}

{(_isNewAccount ? "Set your password" : "Reset your password")}:
{_resetUrl}

This link can be used once and expires in {_validHours} hours.

If you weren't expecting this email you can ignore it — your password stays
unchanged until the link is used.";
    }
}
