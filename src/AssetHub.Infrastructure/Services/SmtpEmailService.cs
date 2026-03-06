using System.Net;
using System.Net.Mail;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// SMTP-based email service implementation.
/// Wraps send operations with a Polly resilience pipeline for retry on transient failures.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly ResiliencePipeline _pipeline;

    public SmtpEmailService(
        IOptions<EmailSettings> settings,
        ILogger<SmtpEmailService> logger,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _settings = settings.Value;
        _logger = logger;
        _pipeline = pipelineProvider.GetPipeline("smtp");
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
            _logger.LogWarning("No valid recipients provided for email: {Subject}", template.Subject);
            return;
        }

        if (!_settings.Enabled)
        {
            _logger.LogInformation("Email sending is disabled. Would have sent '{Subject}' to {RecipientCount} recipient(s)", template.Subject, recipientList.Count);
            return;
        }

        await _pipeline.ExecuteAsync(async ct =>
        {
            using var client = CreateSmtpClient();
            using var message = CreateMailMessage(recipientList, template);

            await client.SendMailAsync(message, ct);
        }, cancellationToken);

        _logger.LogInformation("Successfully sent email '{Subject}' to {RecipientCount} recipient(s)", template.Subject, recipientList.Count);
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
            Subject = template.Subject
        };

        // Add plain text alternative first (lower priority)
        var plainTextView = AlternateView.CreateAlternateViewFromString(
            template.GetPlainTextBody(),
            System.Text.Encoding.UTF8,
            "text/plain");
        message.AlternateViews.Add(plainTextView);

        // Add HTML view (higher priority - will be displayed by clients that support it)
        var htmlView = AlternateView.CreateAlternateViewFromString(
            template.GetHtmlBody(),
            System.Text.Encoding.UTF8,
            "text/html");
        message.AlternateViews.Add(htmlView);

        foreach (var recipient in recipients)
        {
            message.To.Add(new MailAddress(recipient));
        }

        return message;
    }
}
