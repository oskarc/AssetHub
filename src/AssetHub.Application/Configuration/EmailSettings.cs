namespace AssetHub.Application.Configuration;

/// <summary>
/// Configuration settings for the email service.
/// </summary>
public class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>
    /// SMTP server hostname.
    /// </summary>
    public string SmtpHost { get; set; } = "";

    /// <summary>
    /// SMTP server port (default: 587 for TLS, 465 for SSL, 25 for unencrypted).
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// SMTP username for authentication.
    /// </summary>
    public string? SmtpUsername { get; set; }

    /// <summary>
    /// SMTP password for authentication.
    /// </summary>
    public string? SmtpPassword { get; set; }

    /// <summary>
    /// Whether to use SSL/TLS for the SMTP connection.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// The sender's email address.
    /// </summary>
    public string FromAddress { get; set; } = "";

    /// <summary>
    /// The sender's display name.
    /// </summary>
    public string FromName { get; set; } = "AssetHub";

    /// <summary>
    /// Whether email sending is enabled. When false, emails are logged but not sent.
    /// </summary>
    public bool Enabled { get; set; } = false;
}
