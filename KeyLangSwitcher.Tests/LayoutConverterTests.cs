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
    public void AutoConvert_WholeSelection_ConvertsPunctuationOneToOne()
    {
        // 1-в-1 конверсия выделения: запятая (',' = 'б') и апостроф маппятся корректно.
        // Это ключевой сценарий: выделил → сконвертировалось целиком.
        Assert.Equal("скажи где взять ошибку импорта",
            LayoutConverter.ToRussian("crf;b ult dpznm jib,re bvgjhnf"));
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

    [Fact]
    public void ToggleCase_LowersWhenAnyUpper_UppersWhenAllLower()
    {
        Assert.Equal("hello", SelectionConverter.Toggle("Hello"));
        Assert.Equal("HELLO", SelectionConverter.Toggle("hello"));
        Assert.Equal("привет мир", SelectionConverter.Toggle("Привет Мир"));
    }
}
