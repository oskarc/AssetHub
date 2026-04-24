using AssetHub.Application.Helpers;

namespace AssetHub.Tests.Helpers;

public class MentionParserTests
{
    [Fact]
    public void ExtractUsernames_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(MentionParser.ExtractUsernames(null));
        Assert.Empty(MentionParser.ExtractUsernames(""));
        Assert.Empty(MentionParser.ExtractUsernames("   "));
    }

    [Fact]
    public void ExtractUsernames_NoMentions_ReturnsEmpty()
    {
        Assert.Empty(MentionParser.ExtractUsernames("just a plain comment with no mentions"));
        Assert.Empty(MentionParser.ExtractUsernames("email like foo@example.com is not a mention"));
    }

    [Fact]
    public void ExtractUsernames_SingleMention_AtStart_IsExtracted()
    {
        var result = MentionParser.ExtractUsernames("@alice take a look at this");
        Assert.Equal(new[] { "alice" }, result);
    }

    [Fact]
    public void ExtractUsernames_AfterWhitespace_IsExtracted()
    {
        var result = MentionParser.ExtractUsernames("hey @bob check this");
        Assert.Equal(new[] { "bob" }, result);
    }

    [Fact]
    public void ExtractUsernames_AfterPunctuation_IsExtracted()
    {
        var result = MentionParser.ExtractUsernames("pinging (@carol) and [@dave] here");
        Assert.Equal(new[] { "carol", "dave" }, result);
    }

    [Fact]
    public void ExtractUsernames_MultipleMentions_AreDistinctAndOrdered()
    {
        var result = MentionParser.ExtractUsernames("hey @alice and @bob, also @alice again");
        Assert.Equal(new[] { "alice", "bob" }, result);
    }

    [Fact]
    public void ExtractUsernames_EmbeddedInEmail_IsNotExtracted()
    {
        var result = MentionParser.ExtractUsernames("contact alice@example.com for details");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractUsernames_DotsDashesUnderscores_AreAllowed()
    {
        var result = MentionParser.ExtractUsernames("ping @alice.smith and @bob-jones and @carol_doe");
        Assert.Equal(new[] { "alice.smith", "bob-jones", "carol_doe" }, result);
    }

    [Fact]
    public void ExtractUsernames_LongerThan32Chars_IsTruncatedAtMaxLength()
    {
        // Regex caps at 32 chars — anything past the cap is treated as trailing text.
        var name = new string('a', 40);
        var result = MentionParser.ExtractUsernames($"hi @{name}");
        Assert.Single(result);
        Assert.Equal(new string('a', 32), result[0]);
    }
}
