using System.Security.Cryptography;
using AssetHub.Application.Configuration;
using AssetHub.Infrastructure.Services.Watermarking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit tests for <see cref="RecipientCryptoService"/> (T5-WMK-01 G-44).
/// PII round-trip via DataProtection + indexable HMAC properties — pure, no infra.
/// </summary>
public class RecipientCryptoServiceTests
{
    private static RecipientCryptoService Create(string? hmacKeyBase64 = null)
    {
        var key = hmacKeyBase64 ?? GenerateHmacKey();
        return new RecipientCryptoService(
            new EphemeralDataProtectionProvider(),
            Options.Create(new WatermarkSettings { HmacKeyBase64 = key }),
            NullLogger<RecipientCryptoService>.Instance);
    }

    private static string GenerateHmacKey()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        var sut = Create();
        var plaintext = "alice@example.com";

        var cipher = sut.Encrypt(plaintext);
        var decrypted = sut.Decrypt(cipher);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_NonEmpty_ProducesCipherDifferentFromPlaintext()
    {
        var sut = Create();
        var plaintext = "alice@example.com";

        var cipher = sut.Encrypt(plaintext);

        Assert.NotEmpty(cipher);
        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes(plaintext), cipher);
    }

    [Fact]
    public void Decrypt_Empty_ReturnsNull()
    {
        var sut = Create();

        Assert.Null(sut.Decrypt([]));
    }

    [Fact]
    public void Decrypt_RandomBytes_ReturnsNull_NoThrow()
    {
        var sut = Create();
        var corrupt = new byte[64];
        RandomNumberGenerator.Fill(corrupt);

        // Key-rotation drift / corruption surfaces as null, not exception —
        // the verify endpoint depends on this contract to render "PII unavailable".
        var result = sut.Decrypt(corrupt);

        Assert.Null(result);
    }

    [Fact]
    public void Hash_SameInput_DeterministicAndIndexable()
    {
        var sut = Create();

        var h1 = sut.Hash("alice@example.com");
        var h2 = sut.Hash("alice@example.com");

        Assert.Equal(h1, h2);
        Assert.Equal(32, h1.Length); // SHA-256
    }

    [Fact]
    public void Hash_DifferentInputs_ProduceDifferentHashes()
    {
        var sut = Create();

        var h1 = sut.Hash("alice@example.com");
        var h2 = sut.Hash("bob@example.com");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Hash_DifferentKeys_ProduceDifferentHashes()
    {
        var sut1 = Create();
        var sut2 = Create();

        var h1 = sut1.Hash("alice@example.com");
        var h2 = sut2.Hash("alice@example.com");

        // Two services with independently generated keys must not produce the
        // same HMAC for the same plaintext — proves the key is actually keying the hash.
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Constructor_MissingHmacKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Create(""));
    }

    [Fact]
    public void Constructor_InvalidBase64_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Create("not-base64-!!!"));
    }

    [Fact]
    public void Constructor_WrongKeyLength_Throws()
    {
        // Anything other than 32 decoded bytes must reject — guards against operator
        // pasting a partial / wrong-purpose key without validation.
        var shortKey = Convert.ToBase64String(new byte[16]);
        Assert.Throws<InvalidOperationException>(() => Create(shortKey));
    }
}
