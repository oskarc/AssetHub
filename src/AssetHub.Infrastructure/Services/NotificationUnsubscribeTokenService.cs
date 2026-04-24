using System.Text;
using System.Text.Json;
using AssetHub.Application;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class NotificationUnsubscribeTokenService(
    IDataProtectionProvider dataProtection,
    ILogger<NotificationUnsubscribeTokenService> logger) : INotificationUnsubscribeTokenService
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(
        Constants.DataProtection.NotificationUnsubscribeProtector);

    public string CreateToken(string userId, string category, string stamp)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(category);
        ArgumentException.ThrowIfNullOrEmpty(stamp);

        var payload = new Payload(userId, category, stamp);
        var json = JsonSerializer.Serialize(payload);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(json));
        return Base64UrlEncode(protectedBytes);
    }

    public UnsubscribeTokenPayload? TryParseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var cipher = Base64UrlDecode(token);
            var json = Encoding.UTF8.GetString(_protector.Unprotect(cipher));
            var payload = JsonSerializer.Deserialize<Payload>(json);
            if (payload is null
                || string.IsNullOrWhiteSpace(payload.UserId)
                || string.IsNullOrWhiteSpace(payload.Category)
                || string.IsNullOrWhiteSpace(payload.Stamp))
                return null;
            return new UnsubscribeTokenPayload(payload.UserId, payload.Category, payload.Stamp);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unsubscribe token failed to parse");
            return null;
        }
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

    private sealed record Payload(string UserId, string Category, string Stamp);
}
