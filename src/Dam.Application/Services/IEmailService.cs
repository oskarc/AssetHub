using Dam.Application.Services.Email;

namespace Dam.Application.Services;

/// <summary>
/// Service for sending emails.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email to a single recipient.
    /// </summary>
    /// <param name="to">The recipient's email address.</param>
    /// <param name="template">The email template to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendEmailAsync(string to, IEmailTemplate template, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email to multiple recipients.
    /// </summary>
    /// <param name="recipients">The recipients' email addresses.</param>
    /// <param name="template">The email template to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendEmailAsync(IEnumerable<string> recipients, IEmailTemplate template, CancellationToken cancellationToken = default);
}
