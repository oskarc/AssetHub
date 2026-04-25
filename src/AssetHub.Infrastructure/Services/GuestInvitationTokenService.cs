using System.Security.Cryptography;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class GuestInvitationTokenService(IDataProtectionProvider dataProtection)
    : IGuestInvitationTokenService
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(
        Constants.DataProtection.GuestInvitationProtector);

    public GuestInvitationToken Generate(Guid invitationId)
    {
        // Plaintext = signed-and-encoded invitation id. Treat it as opaque
        // on the wire — no manual base64-decoding by the receiver.
        var plaintext = Base64UrlEncode(_protector.Protect(invitationId.ToByteArray()));
        return new GuestInvitationToken(plaintext, HashToken(plaintext));
    }

    public Guid? TryParse(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var protectedBytes = Base64UrlDecode(token);
            var idBytes = _protector.Unprotect(protectedBytes);
            return new Guid(idBytes);
        }
        catch (Exception)
        {
            // Tampered / wrong key ring / not base64url — all surface as
            // "no, this isn't a valid token". The caller maps to NotFound.
            return null;
        }
    }

    public string HashToken(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexStringLower(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
