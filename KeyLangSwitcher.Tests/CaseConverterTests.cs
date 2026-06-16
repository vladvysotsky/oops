using KeyLangSwitcher.Core;
using Xunit;

namespace KeyLangSwitcher.Tests;

public class CaseConverterTests
{
    [Theory]
    [InlineData("Hello", "hello")]          // есть заглавная → к нижнему
    [InlineData("Привет Мир", "привет мир")]
    [InlineData("ALL_UPPER", "all_upper")]
    public void Toggle_LowersWhenAnyUpperPresent(string input, string expected)
    {
        Assert.Equal(expected, CaseConverter.Toggle(input));
    }

    [Theory]
    [InlineData("hello", "HELLO")]
    [InlineData("привет мир", "ПРИВЕТ МИР")]
    [InlineData("123 abc", "123 ABC")]
    public void Toggle_UppersWhenAllLower(string input, string expected)
    {
        Assert.Equal(expected, CaseConverter.Toggle(input));
    }
}
