namespace Dam.Application.Services.Email.Templates;

/// <summary>
/// Welcome email sent to newly created users with their login credentials 
/// and instructions to change their password on first login.
/// </summary>
public class WelcomeEmailTemplate : EmailTemplateBase
{
    private readonly string _username;
    private readonly string _temporaryPassword;
    private readonly string _loginUrl;
    private readonly bool _requirePasswordChange;
    private readonly string? _createdByAdmin;

    public WelcomeEmailTemplate(
        string username,
        string temporaryPassword,
        string loginUrl,
        bool requirePasswordChange = true,
        string? createdByAdmin = null)
    {
        _username = username;
        _temporaryPassword = temporaryPassword;
        _loginUrl = loginUrl;
        _requirePasswordChange = requirePasswordChange;
        _createdByAdmin = createdByAdmin;
    }

    public override string Subject => "Welcome to AssetHub — Your account has been created";

    protected override string GetContentHtml()
    {
        var passwordChangeNotice = _requirePasswordChange
            ? @"<p style=""color: #e65100; font-weight: 600;"">
                   ⚠️ You will be required to change your password when you first log in.
               </p>"
            : @"<p style=""color: #666; font-size: 14px;"">
                   We recommend changing your password after your first login for security.
               </p>";

        var createdBy = !string.IsNullOrEmpty(_createdByAdmin)
            ? $"<p>An administrator (<strong>{EscapeHtml(_createdByAdmin)}</strong>) has created an account for you on AssetHub.</p>"
            : "<p>An account has been created for you on AssetHub.</p>";

        return $@"
            <h2 style=""margin-top: 0;"">Welcome to AssetHub! 👋</h2>
            
            {createdBy}
            
            <p>Use the credentials below to log in and get started.</p>
            
            <div class=""info-box"">
                <div class=""info-box-label"">Username</div>
                <div class=""info-box-value"" style=""font-size: 16px; font-weight: bold;"">{EscapeHtml(_username)}</div>
            </div>
            
            <div class=""password-box"">
                <div class=""info-box-label"">Temporary Password</div>
                <div class=""info-box-value"" style=""font-size: 18px; font-weight: bold;"">{EscapeHtml(_temporaryPassword)}</div>
                <p class=""warning-text"">🔒 Do not share this password. It is for your use only.</p>
            </div>
            
            {passwordChangeNotice}
            
            <div style=""text-align: center;"">
                <a href=""{EscapeHtml(_loginUrl)}"" class=""button"">Log In to AssetHub</a>
            </div>
            
            <h3 style=""margin-top: 32px;"">Getting Started</h3>
            <ol style=""color: #555; line-height: 1.8;"">
                <li>Click the login button above or go to <a href=""{EscapeHtml(_loginUrl)}"">{EscapeHtml(_loginUrl)}</a></li>
                <li>Enter your username and temporary password</li>
                {(_requirePasswordChange ? "<li><strong>Choose a new secure password</strong> when prompted</li>" : "")}
                <li>Start browsing your collections and assets</li>
            </ol>
            
            <p style=""color: #666; font-size: 14px; margin-top: 24px;"">
                If you did not expect this account or believe it was created in error, 
                please contact your administrator.
            </p>";
    }

    protected override string GetContentPlainText()
    {
        var passwordChangeNotice = _requirePasswordChange
            ? "IMPORTANT: You will be required to change your password when you first log in."
            : "We recommend changing your password after your first login for security.";

        var createdBy = !string.IsNullOrEmpty(_createdByAdmin)
            ? $"An administrator ({_createdByAdmin}) has created an account for you on AssetHub."
            : "An account has been created for you on AssetHub.";

        return $@"Welcome to AssetHub!

{createdBy}

Use the credentials below to log in and get started.

USERNAME:
{_username}

TEMPORARY PASSWORD:
{_temporaryPassword}

Do not share this password. It is for your use only.

{passwordChangeNotice}

GETTING STARTED:
1. Go to {_loginUrl}
2. Enter your username and temporary password
{(_requirePasswordChange ? "3. Choose a new secure password when prompted\n4. Start browsing your collections and assets" : "3. Start browsing your collections and assets")}

LOGIN URL:
{_loginUrl}

If you did not expect this account or believe it was created in error,
please contact your administrator.";
    }
}
