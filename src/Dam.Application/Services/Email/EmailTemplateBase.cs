namespace Dam.Application.Services.Email;

/// <summary>
/// Base class for email templates providing common styling and structure.
/// Extend this class to create new email templates with consistent branding.
/// </summary>
public abstract class EmailTemplateBase : IEmailTemplate
{
    public abstract string Subject { get; }

    /// <summary>
    /// Override this to provide the main content of the email.
    /// </summary>
    protected abstract string GetContentHtml();

    /// <summary>
    /// Override this to provide the plain text version of the content.
    /// </summary>
    protected abstract string GetContentPlainText();

    /// <summary>
    /// The application name shown in the email header.
    /// </summary>
    protected virtual string AppName => "AssetHub";

    /// <summary>
    /// The primary brand color used in the email.
    /// </summary>
    protected virtual string PrimaryColor => "#1976D2";

    /// <summary>
    /// The footer text shown at the bottom of the email.
    /// </summary>
    protected virtual string FooterText => "This is an automated message from AssetHub. Please do not reply directly to this email.";

    public string GetHtmlBody()
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{EscapeHtml(Subject)}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            line-height: 1.6;
            color: #333333;
            margin: 0;
            padding: 0;
            background-color: #f5f5f5;
        }}
        .email-container {{
            max-width: 600px;
            margin: 0 auto;
            background-color: #ffffff;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
        }}
        .email-header {{
            background-color: {PrimaryColor};
            color: #ffffff;
            padding: 24px;
            text-align: center;
        }}
        .email-header h1 {{
            margin: 0;
            font-size: 24px;
            font-weight: 600;
        }}
        .email-body {{
            padding: 32px 24px;
        }}
        .email-footer {{
            background-color: #f9f9f9;
            padding: 16px 24px;
            text-align: center;
            font-size: 12px;
            color: #666666;
            border-top: 1px solid #eeeeee;
        }}
        .button {{
            display: inline-block;
            background-color: {PrimaryColor};
            color: #ffffff !important;
            text-decoration: none;
            padding: 12px 24px;
            border-radius: 6px;
            font-weight: 500;
            margin: 16px 0;
        }}
        .button:hover {{
            background-color: #1565C0;
        }}
        .info-box {{
            background-color: #f5f5f5;
            border-radius: 6px;
            padding: 16px;
            margin: 16px 0;
        }}
        .info-box-label {{
            font-size: 12px;
            color: #666666;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-bottom: 4px;
        }}
        .info-box-value {{
            font-family: 'Courier New', monospace;
            font-size: 14px;
            color: #333333;
            word-break: break-all;
        }}
        .password-box {{
            background-color: #fff3e0;
            border: 1px solid #ffb74d;
            border-radius: 6px;
            padding: 16px;
            margin: 16px 0;
        }}
        .warning-text {{
            color: #e65100;
            font-size: 12px;
            margin-top: 8px;
        }}
    </style>
</head>
<body>
    <div style=""padding: 20px;"">
        <div class=""email-container"">
            <div class=""email-header"">
                <h1>{EscapeHtml(AppName)}</h1>
            </div>
            <div class=""email-body"">
                {GetContentHtml()}
            </div>
            <div class=""email-footer"">
                {EscapeHtml(FooterText)}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    public string GetPlainTextBody()
    {
        return $@"{AppName}
{new string('=', AppName.Length)}

{GetContentPlainText()}

---
{FooterText}";
    }

    /// <summary>
    /// Helper method to escape HTML special characters.
    /// </summary>
    protected static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
