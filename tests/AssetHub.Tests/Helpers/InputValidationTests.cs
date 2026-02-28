using AssetHub.Application;

namespace AssetHub.Tests.Helpers;

/// <summary>
/// Unit tests for InputValidation helpers.
/// </summary>
public class InputValidationTests
{
    // ── ValidateSharePassword ────────────────────────────────────────────────

    [Fact]
    public void ValidateSharePassword_NullValue_ReturnsError()
    {
        var result = InputValidation.ValidateSharePassword(null);

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSharePassword_EmptyString_ReturnsError()
    {
        var result = InputValidation.ValidateSharePassword("");

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSharePassword_WhitespaceOnly_ReturnsError()
    {
        var result = InputValidation.ValidateSharePassword("   ");

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateSharePassword_TooShort_ReturnsError()
    {
        // One character below the minimum
        var tooShort = new string('a', Constants.Limits.MinSharePasswordLength - 1);

        var result = InputValidation.ValidateSharePassword(tooShort);

        Assert.NotNull(result);
        Assert.Contains(Constants.Limits.MinSharePasswordLength.ToString(), result);
    }

    [Fact]
    public void ValidateSharePassword_ExactMinimumLength_ReturnsNull()
    {
        var minLength = new string('a', Constants.Limits.MinSharePasswordLength);

        var result = InputValidation.ValidateSharePassword(minLength);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateSharePassword_LongPassword_ReturnsNull()
    {
        var result = InputValidation.ValidateSharePassword("correct-horse-battery-staple-99!");

        Assert.Null(result);
    }

    // ── ValidateAssetTitle ───────────────────────────────────────────────────

    [Fact]
    public void ValidateAssetTitle_NullValue_ReturnsError()
    {
        var result = InputValidation.ValidateAssetTitle(null);

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateAssetTitle_EmptyString_ReturnsError()
    {
        var result = InputValidation.ValidateAssetTitle("");

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateAssetTitle_WhitespaceOnly_ReturnsError()
    {
        var result = InputValidation.ValidateAssetTitle("   ");

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateAssetTitle_TooLong_ReturnsError()
    {
        var tooLong = new string('a', 256);

        var result = InputValidation.ValidateAssetTitle(tooLong);

        Assert.NotNull(result);
    }

    [Fact]
    public void ValidateAssetTitle_ExactMaxLength_ReturnsNull()
    {
        var maxLength = new string('a', 255);

        var result = InputValidation.ValidateAssetTitle(maxLength);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateAssetTitle_NormalTitle_ReturnsNull()
    {
        var result = InputValidation.ValidateAssetTitle("My Great Asset");

        Assert.Null(result);
    }
}
