namespace AssetHub.Application.Services.Email;

/// <summary>
/// Base interface for all email templates.
/// Implement this interface to create new email types.
/// </summary>
public interface IEmailTemplate
{
    /// <summary>
    /// The subject line of the email.
    /// </summary>
    string Subject { get; }

    /// <summary>
    /// Generates the HTML body of the email.
    /// </summary>
    string GetHtmlBody();

    /// <summary>
    /// Generates the plain text body of the email (for clients that don't support HTML).
    /// </summary>
    string GetPlainTextBody();
}
