using AssetHub.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AssetHub.Tests.Services;

public class WebhookSecretProtectorTests
{
    private static WebhookSecretProtector CreateSut(IDataProtectionProvider? provider = null)
        => new(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void GeneratePlaintext_ReturnsUrlSafeBase64()
    {
        var sut = CreateSut();
        var secret = sut.GeneratePlaintext();

        Assert.False(string.IsNullOrWhiteSpace(secret));
        // base64url tokens never contain +, /, =
        Assert.DoesNotContain('+', secret);
        Assert.DoesNotContain('/', secret);
        Assert.DoesNotContain('=', secret);
        // 32 bytes encoded with stripped padding ≈ 43 chars
        Assert.Equal(43, secret.Length);
    }

    [Fact]
    public void Protect_ThenUnprotect_RoundTrips()
    {
        var sut = CreateSut();
        var secret = sut.GeneratePlaintext();

        var ciphertext = sut.Protect(secret);
        var decrypted = sut.Unprotect(ciphertext);

        Assert.Equal(secret, decrypted);
        Assert.NotEqual(secret, ciphertext);
    }

    [Fact]
    public void Protect_ProducesDifferentCiphertextEachTime()
    {
        var sut = CreateSut();
        var secret = "fixed-secret";

        var c1 = sut.Protect(secret);
        var c2 = sut.Protect(secret);

        // Data Protection adds random IVs — ciphertexts differ but both decrypt.
        Assert.NotEqual(c1, c2);
        Assert.Equal(secret, sut.Unprotect(c1));
        Assert.Equal(secret, sut.Unprotect(c2));
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

    [Fact]
    public void Generate_ReturnsDifferentSecretsEachCall()
    {
        var sut = CreateSut();
        var a = sut.GeneratePlaintext();
        var b = sut.GeneratePlaintext();
        Assert.NotEqual(a, b);
    }
}
