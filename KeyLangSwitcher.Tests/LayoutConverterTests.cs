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
        var (result, _, anyChange, anyKnown) = LayoutConverter.AutoConvertPerWord("это правильный текст");
        Assert.Equal("это правильный текст", result);
        Assert.False(anyChange);
        Assert.True(anyKnown); // 'это' и 'текст' опознаются как RU
    }

    [Fact]
    public void AutoConvertPerWord_FixesOnlyWrongLayoutWord()
    {
        var (result, dir, anyChange, _) = LayoutConverter.AutoConvertPerWord("это vfvf слово");
        Assert.Equal("это мама слово", result);
        Assert.True(anyChange);
        Assert.Equal(LayoutConverter.Direction.ToRu, dir);
    }

    [Fact]
    public void AutoConvertPerWord_PreservesPunctuationAndSeparators()
    {
        var (result, _, _, _) = LayoutConverter.AutoConvertPerWord("hello, vfvf!");
        Assert.Equal("hello, мама!", result);
    }

    [Fact]
    public void AutoConvertPerWord_SemicolonInsideWord_TreatedAsRuLetter()
    {
        // 'hfccr;b' — пользователь набирал "расскжи" в EN-раскладке.
        // Символ ';' на EN-раскладке соответствует RU-букве 'ж', поэтому он
        // должен быть частью слова, а не разделителем. Результат — 'расскжи'.
        var (result, _, anyChange, _) = LayoutConverter.AutoConvertPerWord("hfccr;b");
        Assert.True(anyChange);
        Assert.Equal("расскжи", result);
    }

    [Fact]
    public void AutoConvertPerWord_BracketsAndApostropheAreRuLetters()
    {
        // [ → х, ] → ъ, ' → э — тоже летеры на RU-раскладке.
        var (result, _, _, _) = LayoutConverter.AutoConvertPerWord("[fnf'");
        Assert.Equal("хатаэ", result);
    }

    [Fact]
    public void AutoConvertPerWord_RealPunctuation_StillSplitsWords()
    {
        // ',' '.' '/' остаются настоящими разделителями.
        var (result, _, _, _) = LayoutConverter.AutoConvertPerWord("vfvf, gfgf.");
        Assert.Equal("мама, папа.", result);
    }

    [Fact]
    public void AutoConvertPerWord_CorrectRussianWithOneBadWord_KeepsRestIntact()
    {
        // Регрессионный тест из бага пользователя: была проблема, что fallback на
        // whole-buffer перевирал весь правильный русский текст в EN-гибериш, если
        // per-word ничего не сконвертировал. Теперь fallback подавляется когда
        // в буфере есть опознанные слова.
        var (result, _, _, anyKnown) = LayoutConverter.AutoConvertPerWord("проведи симуляцию и сохрани");
        Assert.True(anyKnown);
        Assert.Equal("проведи симуляцию и сохрани", result);
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
