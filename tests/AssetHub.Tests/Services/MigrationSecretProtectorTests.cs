using AssetHub.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit coverage for <see cref="MigrationSecretProtector"/> — uses
/// <see cref="EphemeralDataProtectionProvider"/> so the tests don't need a
/// real key ring on disk.
/// </summary>
public class MigrationSecretProtectorTests
{
    private static MigrationSecretProtector CreateSut(IDataProtectionProvider? provider = null)
        => new(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void Protect_ThenUnprotect_RoundTripsOriginalPlaintext()
    {
        var sut = CreateSut();
        const string secret = "super-secret-access-key-material";

        var ciphertext = sut.Protect(secret);
        var decrypted = sut.Unprotect(ciphertext);

        Assert.Equal(secret, decrypted);
    }

    [Fact]
    public void Protect_ReturnsBase64CiphertextDistinctFromPlaintext()
    {
        var sut = CreateSut();
        const string secret = "plaintext-value";

        var ciphertext = sut.Protect(secret);

        Assert.NotEqual(secret, ciphertext);
        // Base64 tolerates padding; we just need it to decode cleanly.
        Convert.FromBase64String(ciphertext);
    }

    [Fact]
    public void Unprotect_WithDifferentKeyRing_Throws()
    {
        var sut1 = CreateSut();
        var sut2 = CreateSut();
        var ciphertext = sut1.Protect("the-secret");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => sut2.Unprotect(ciphertext));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Protect_EmptyOrNull_Throws(string? plaintext)
    {
        var sut = CreateSut();

        Assert.ThrowsAny<ArgumentException>(() => sut.Protect(plaintext!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Unprotect_EmptyOrNull_Throws(string? payload)
    {
        var sut = CreateSut();

        Assert.ThrowsAny<ArgumentException>(() => sut.Unprotect(payload!));
    }
}
