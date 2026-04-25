using System.Security.Cryptography;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class WebhookSecretProtector(IDataProtectionProvider dataProtection) : IWebhookSecretProtector
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(
        Constants.DataProtection.WebhookSecretProtector);

    public string GeneratePlaintext()
    {
        // 32 bytes of CSPRNG entropy = 256 bits, plenty for HMAC-SHA256.
        // base64url so admins can copy it cleanly into config files / env vars.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedPayload)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedPayload);
        var cipher = Convert.FromBase64String(protectedPayload);
        return Encoding.UTF8.GetString(_protector.Unprotect(cipher));
    }
}
