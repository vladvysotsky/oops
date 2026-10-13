using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class SnippetsTests
{
    private static SnippetMatcher Matcher(params (string Trigger, string Text)[] snippets)
        => new(snippets.Select(s => new Snippet { Trigger = s.Trigger, Text = s.Text }), LayoutPair.BuiltIn);

    [Theory]
    // Те же клавиши в другой раскладке и другом регистре — тот же сниппет:
    // иначе он молчал бы ровно тогда, когда раскладка перепутана.
    [InlineData("!ee")]
    [InlineData("!уу")]
    [InlineData("!EE")]
    [InlineData("!Уу")]
    public void TriggerMatchesRegardlessOfLayoutAndCase(string typed)
    {
        Assert.True(Matcher(("!ee", "me@example.com")).TryMatch(typed, out var text));
        Assert.Equal("me@example.com", text);
    }

    [Fact]
    public void TriggerWrittenInTheSecondLayoutMatchesTheFirst()
    {
        Assert.True(Matcher(("адр", "Moscow, Tverskaya 1")).TryMatch("flh", out var text));
        Assert.Equal("Moscow, Tverskaya 1", text);
    }

    [Fact]
    public void OtherWordsDoNotMatch()
    {
        var matcher = Matcher(("!ee", "me@example.com"));
        Assert.False(matcher.TryMatch("!e", out _));
        Assert.False(matcher.TryMatch("!eee", out _));
        Assert.False(matcher.TryMatch("", out _));
    }

    [Fact]
    public void InvalidSnippetsAreIgnored()
    {
        var matcher = Matcher(("two words", "x"), ("", "x"), ("!n", ""));
        Assert.True(matcher.IsEmpty);
    }

    [Fact]
    public void FirstOfTwoSnippetsOnTheSameKeysWins()
    {
        Assert.True(Matcher(("!ee", "first"), ("!уу", "second")).TryMatch("!ee", out var text));
        Assert.Equal("first", text);
    }

    [Theory]
    [InlineData("hello !ee", "!ee")]
    [InlineData("!ee", "!ee")]
    [InlineData("hello ", "")]
    [InlineData("", "")]
    [InlineData("a\tb", "b")]
    public void LastWordIsWhatFollowsTheLastSpace(string buffer, string expected)
        => Assert.Equal(expected, SnippetMatcher.LastWord(buffer));

    [Theory]
    [InlineData("!ee", true)]
    [InlineData("two words", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TriggerCannotContainSpaces(string? trigger, bool valid)
        => Assert.Equal(valid, SnippetMatcher.IsValidTrigger(trigger));
}
