using System.Net;
using System.Net.Mail;
using Dam.Application.Services;
using Dam.Application.Services.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dam.Infrastructure.Services;

/// <summary>
/// SMTP-based email service implementation.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailSettings> settings, ILogger<SmtpEmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, IEmailTemplate template, CancellationToken cancellationToken = default)
    {
        await SendEmailAsync(new[] { to }, template, cancellationToken);
    }

    public async Task SendEmailAsync(IEnumerable<string> recipients, IEmailTemplate template, CancellationToken cancellationToken = default)
    {
        var recipientList = recipients.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        
        if (!recipientList.Any())
        {
            _logger.LogWarning($"No valid recipients provided for email: {template.Subject}");
            return;
        }

        if (!_settings.Enabled)
        {
            _logger.LogInformation($"Email sending is disabled. Would have sent '{template.Subject}' to: {string.Join(", ", recipientList)}");
            return;
        }

        try
        {
            using var client = CreateSmtpClient();
            using var message = CreateMailMessage(recipientList, template);

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation($"Successfully sent email '{template.Subject}' to {recipientList.Count} recipient(s)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send email '{template.Subject}' to: {string.Join(", ", recipientList)}");
            throw;
        }
    }

    private SmtpClient CreateSmtpClient()
    {
        var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrEmpty(_settings.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword);
        }

        return client;
    }

    private MailMessage CreateMailMessage(IEnumerable<string> recipients, IEmailTemplate template)
    {
        var message = new MailMessage
        {
            From = new MailAddress(_settings.FromAddress, _settings.FromName),
            Subject = template.Subject,
            IsBodyHtml = true,
            Body = template.GetHtmlBody()
        };

        // Add plain text alternative for email clients that don't support HTML
        var plainTextView = AlternateView.CreateAlternateViewFromString(
            template.GetPlainTextBody(),
            null,
            "text/plain");
        message.AlternateViews.Add(plainTextView);

        foreach (var recipient in recipients)
        {
            message.To.Add(new MailAddress(recipient));
        }

        return message;
    }
}
