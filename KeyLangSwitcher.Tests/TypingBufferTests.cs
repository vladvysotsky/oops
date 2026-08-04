using KeyLangSwitcher.Core;
using Xunit;

namespace KeyLangSwitcher.Tests;

public class TypingBufferTests
{
    [Fact]
    public void Append_And_Backspace_TrackTypedText()
    {
        var b = new TypingBuffer();
        foreach (var c in "abc") b.Append(c);
        Assert.Equal("abc", b.Snapshot());
        b.Backspace();
        Assert.Equal("ab", b.Snapshot());
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var b = new TypingBuffer();
        b.Append('x');
        b.Clear();
        Assert.Equal(0, b.Length);
    }

    [Theory]
    [InlineData("привет как дела", 1, 11)]  // "дела"
    [InlineData("привет как дела", 2, 7)]   // "как дела"
    [InlineData("привет как дела", 3, 0)]   // всё
    [InlineData("привет как дела", 9, 0)]   // больше, чем слов — всё
    public void StartOfLastWords_FindsWordBoundaries(string text, int words, int expected)
    {
        Assert.Equal(expected, TypingBuffer.StartOfLastWords(text, words));
    }

    [Fact]
    public void StartOfLastWords_HandlesTrailingSpace()
    {
        // Хвостовой пробел не считается словом: последнее слово всё ещё "дела".
        Assert.Equal(11, TypingBuffer.StartOfLastWords("привет как дела ", 1));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("  one   two  ", 2)]
    [InlineData("a b c d", 4)]
    public void CountWords_CountsNonWhitespaceRuns(string text, int expected)
    {
        Assert.Equal(expected, TypingBuffer.CountWords(text));
    }
}
