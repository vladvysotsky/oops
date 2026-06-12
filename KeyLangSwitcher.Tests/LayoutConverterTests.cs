using KeyLangSwitcher.Core;
using Xunit;

namespace KeyLangSwitcher.Tests;

public class LayoutConverterTests
{
    [Theory]
    [InlineData("vfvf", "мама")]
    [InlineData("Z nt,z k.,k.", "Я тебя люблю")]
    [InlineData("ghbdtn", "привет")]
    [InlineData("vfvfxrf", "мамачка")]
    [InlineData("", "")]
    public void EnToRu_ConvertsCorrectly(string en, string ru)
    {
        Assert.Equal(ru, LayoutConverter.ToRussian(en));
    }

    [Theory]
    [InlineData("мама", "vfvf")]
    [InlineData("Я тебя люблю", "Z nt,z k.,k.")]
    [InlineData("привет", "ghbdtn")]
    public void RuToEn_ConvertsCorrectly(string ru, string en)
    {
        Assert.Equal(en, LayoutConverter.ToEnglish(ru));
    }

    [Fact]
    public void Roundtrip_EnRuEn_Stable()
    {
        const string original = "Hello, World!";
        var ru = LayoutConverter.ToRussian(original);
        var back = LayoutConverter.ToEnglish(ru);
        Assert.Equal(original, back);
    }

    [Fact]
    public void Roundtrip_RuEnRu_Stable()
    {
        const string original = "Привет, мир!";
        var en = LayoutConverter.ToEnglish(original);
        var back = LayoutConverter.ToRussian(en);
        Assert.Equal(original, back);
    }

    [Theory]
    [InlineData("vfvf", LayoutConverter.Direction.ToRu, "мама")]
    [InlineData("мама", LayoutConverter.Direction.ToEn, "vfvf")]
    [InlineData("123 !@#", LayoutConverter.Direction.None, "123 !@#")]
    public void AutoConvert_PicksDirectionByMajorityCharset(string input, LayoutConverter.Direction expectedDir, string expected)
    {
        var (result, dir) = LayoutConverter.AutoConvertWithDirection(input);
        Assert.Equal(expectedDir, dir);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AutoConvertPerWord_LeavesCorrectTextAlone()
    {
        // Правильно набранный RU текст должен остаться нетронутым.
        var (result, _, anyChange) = LayoutConverter.AutoConvertPerWord("это правильный текст");
        Assert.Equal("это правильный текст", result);
        Assert.False(anyChange);
    }

    [Fact]
    public void AutoConvertPerWord_FixesOnlyWrongLayoutWord()
    {
        // Смешанный текст: основная часть в правильной раскладке, одно слово в EN
        // (на самом деле RU слово в EN раскладке) — переводится только оно.
        var (result, dir, anyChange) = LayoutConverter.AutoConvertPerWord("это vfvf слово");
        Assert.Equal("это мама слово", result);
        Assert.True(anyChange);
        Assert.Equal(LayoutConverter.Direction.ToRu, dir);
    }

    [Fact]
    public void AutoConvertPerWord_PreservesPunctuationAndSeparators()
    {
        var (result, _, _) = LayoutConverter.AutoConvertPerWord("hello, vfvf!");
        Assert.Equal("hello, мама!", result);
    }

    [Fact]
    public void AutoConvertPerWord_AppliedToUsersExampleStructure()
    {
        // Структурно похоже на пример пользователя: длинный правильный текст +
        // короткий "битый" хвост.
        var input = "Файл это до замены, а - gjckt vfvf";
        var (result, _, anyChange) = LayoutConverter.AutoConvertPerWord(input);
        Assert.True(anyChange);
        Assert.Contains("после", result);
        Assert.Contains("мама", result);
        Assert.Contains("Файл это до замены", result); // RU-часть не тронута
    }

    [Theory]
    [InlineData('@', '"')]
    [InlineData('#', '№')]
    [InlineData('$', ';')]
    [InlineData('^', ':')]
    [InlineData('&', '?')]
    [InlineData('|', '/')]
    [InlineData('?', ',')]
    [InlineData('~', 'Ё')]
    [InlineData('`', 'ё')]
    public void ShiftSymbols_MapPositionally(char en, char ru)
    {
        Assert.Equal(ru.ToString(), LayoutConverter.ToRussian(en.ToString()));
        Assert.Equal(en.ToString(), LayoutConverter.ToEnglish(ru.ToString()));
    }
}
