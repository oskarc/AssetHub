using AssetHub.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit coverage for <see cref="NotificationUnsubscribeTokenService"/>. Backed by
/// <see cref="EphemeralDataProtectionProvider"/> so the tests don't need a
/// real key ring on disk.
/// </summary>
public class NotificationUnsubscribeTokenServiceTests
{
    private static NotificationUnsubscribeTokenService CreateSut(IDataProtectionProvider? provider = null)
        => new(provider ?? new EphemeralDataProtectionProvider(),
            NullLogger<NotificationUnsubscribeTokenService>.Instance);

    [Fact]
    public void CreateToken_ThenTryParseToken_RoundTripsPayload()
    {
        var sut = CreateSut();

        var token = sut.CreateToken("user-123", "mention", "stamp-abc");
        var parsed = sut.TryParseToken(token);

        Assert.NotNull(parsed);
        Assert.Equal("user-123", parsed!.UserId);
        Assert.Equal("mention", parsed.Category);
        Assert.Equal("stamp-abc", parsed.Stamp);
    }

    [Fact]
    public void CreateToken_IsUrlSafe()
    {
        var sut = CreateSut();

        var token = sut.CreateToken("u", "c", "s");

        // base64url uses -, _ instead of +, / and has no padding
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void TryParseToken_TamperedToken_ReturnsNull()
    {
        var sut = CreateSut();
        var token = sut.CreateToken("user-1", "mention", "stamp");

        // Flip one character mid-token
        var tampered = token[..5] + (token[5] == 'A' ? 'B' : 'A') + token[6..];

        Assert.Null(sut.TryParseToken(tampered));
    }

    [Fact]
    public void TryParseToken_TokenFromDifferentKeyRing_ReturnsNull()
    {
        var sut1 = CreateSut();
        var sut2 = CreateSut();
        var token = sut1.CreateToken("u", "c", "s");

        Assert.Null(sut2.TryParseToken(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public void TryParseToken_InvalidInput_ReturnsNull(string input)
    {
        var sut = CreateSut();
        Assert.Null(sut.TryParseToken(input));
    }

    [Theory]
    [InlineData("", "c", "s")]
    [InlineData("u", "", "s")]
    [InlineData("u", "c", "")]
    public void CreateToken_EmptyField_Throws(string user, string category, string stamp)
    {
        var sut = CreateSut();
        Assert.ThrowsAny<ArgumentException>(() => sut.CreateToken(user, category, stamp));
    }
}
