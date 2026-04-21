using System.Text;
using AssetHub.Application;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class MigrationSecretProtector(IDataProtectionProvider dataProtection) : IMigrationSecretProtector
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(
        Constants.DataProtection.MigrationSourceSecretProtector);

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedPayload)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedPayload);
        var cipherBytes = Convert.FromBase64String(protectedPayload);
        var plainBytes = _protector.Unprotect(cipherBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
