using System.Text;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email;
using AssetHub.Application.Services.Email.Templates;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Identity;

/// <summary>
/// Mints an ASP.NET Core Identity password-reset token and emails the link.
/// </summary>
/// <remarks>
/// Security properties this type is responsible for:
/// <list type="bullet">
/// <item>The token is <b>single-use</b> — Identity derives it from the user's
/// security stamp, which <c>ResetPasswordAsync</c> rotates, so a used link
/// cannot be replayed.</item>
/// <item>The token <b>expires</b> — the default data-protection token provider
/// applies <c>DataProtectionTokenProviderOptions.TokenLifespan</c> (24h).</item>
/// <item>The token is <b>never logged</b>. Only the user id appears in logs;
/// the token is Base64Url-encoded straight into the link and passed to the
/// mailer.</item>
/// </list>
/// </remarks>
public sealed class PasswordResetLinkSender(
    UserManager<AppUser> userManager,
    IEmailService emailService,
    IOptions<AppSettings> appSettings,
    ILogger<PasswordResetLinkSender> logger) : IPasswordResetLinkSender<AppUser>
{
    /// <summary>Matches the Identity data-protection token default.</summary>
    private const int TokenValidHours = 24;

    public async Task SendAsync(AppUser user, bool isNewAccount = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning("Cannot send password reset for {UserId} — no email on the account", user.Id);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        // Base64Url so the token survives the query string intact; raw Identity
        // tokens contain characters that would otherwise need double-escaping.
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var baseUrl = appSettings.Value.BaseUrl.TrimEnd('/');
        var link = $"{baseUrl}/reset-password?userId={Uri.EscapeDataString(user.Id)}&token={encoded}";

        await emailService.SendEmailAsync(
            user.Email,
            new PasswordResetEmailTemplate(
                user.DisplayName ?? user.UserName ?? user.Email,
                link,
                TokenValidHours,
                isNewAccount),
            ct);

        // Log the recipient id only — never the token or the assembled link.
        logger.LogInformation("Sent password reset link to user {UserId}", user.Id);
    }

    /// <summary>
    /// Completes a reset. Returns false for an unknown user or a bad/expired
    /// token without distinguishing the two, so the endpoint cannot be used to
    /// probe which accounts exist.
    /// </summary>
    public async Task<(bool Succeeded, string? Error)> ResetAsync(
        string userId, string encodedToken, string newPassword, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            logger.LogInformation("Password reset attempted for unknown user id");
            return (false, null);
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (FormatException ex)
        {
            // A malformed token is indistinguishable from a wrong one to the caller.
            logger.LogInformation(ex, "Password reset attempted with a malformed token for {UserId}", user.Id);
            return (false, null);
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
        {
            logger.LogInformation("Password reset completed for {UserId}", user.Id);
            return (true, null);
        }

        // Password-policy failures are surfaced (the user can act on them);
        // token failures are not (they would confirm the account exists).
        var policyErrors = result.Errors
            .Where(e => !e.Code.Contains("Token", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Description)
            .ToList();

        logger.LogInformation("Password reset failed for {UserId}", user.Id);
        return (false, policyErrors.Count > 0 ? string.Join(" ", policyErrors) : null);
    }
}
