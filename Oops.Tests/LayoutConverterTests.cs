using Oops.Core;
using Xunit;

namespace Oops.Tests;

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
    public void DirectionIsChosenPerWord()
    {
        // Первые четыре слова набраны в EN-раскладке вместо русской, последнее —
        // в русской вместо английской. Одно направление на весь кусок такой
        // текст не чинило вообще.
        var (result, dir) = LayoutConverter.AutoConvertWithDirection(
            "Z djn [jxe pfgecnbnm ЬщвудКшыл");

        Assert.Equal("Я вот хочу запустить ModelRisk", result);
        // Наружу идёт направление последнего слова: каретка в конце, и системную
        // раскладку надо переключить под то, что будут печатать дальше.
        Assert.Equal(LayoutConverter.Direction.ToEn, dir);
    }

    [Fact]
    public void RealForeignWordsSurviveTheConversion()
    {
        // Жалоба пользователя: «appconfig» превращался в «фззсщташп».
        // Оба слова вокруг него полностью латинские, как и оно само, —
        // подсчётом букв их не отличить, различает только язык.
        var (result, _) = LayoutConverter.AutoConvertWithDirection("f lkz xtuj appconfig ye;ty");
        Assert.Equal("а для чего appconfig нужен", result);
    }

    [Theory]
    // Ничего из этого трогать нельзя: одно нажатие превратило бы в мусор,
    // а вернуть повторным нажатием уже не получится.
    [InlineData("https://example.com")]
    [InlineData("config.json")]
    [InlineData("CI/CD")]
    [InlineData("README.md")]
    [InlineData("nginx")]
    [InlineData("useState")]
    public void CorrectTextIsLeftAlone(string input)
    {
        var (result, _) = LayoutConverter.AutoConvertWithDirection(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void SmartSelectionCanBeTurnedOff()
    {
        // Запасной ход для случая, когда модель ошиблась и сочетание молчит.
        try
        {
            LayoutConverter.SmartWordSelection = false;
            var (result, _) = LayoutConverter.AutoConvertWithDirection("appconfig");
            Assert.Equal("фззсщташп", result);
        }
        finally
        {
            LayoutConverter.SmartWordSelection = true;
        }
    }

    [Fact]
    public void WhitespaceIsPreservedExactly()
    {
        // Пословная обработка не имеет права трогать разделители: на них
        // держится арифметика стирания.
        const string input = "  vfvf\t\nvfvf  ";
        var (result, _) = LayoutConverter.AutoConvertWithDirection(input);
        Assert.Equal("  мама\t\nмама  ", result);
        Assert.Equal(input.Length, result.Length);
    }

    [Fact]
    public void WordsWithoutLettersAreLeftAlone()
    {
        var (result, dir) = LayoutConverter.AutoConvertWithDirection("123 vfvf 456");
        Assert.Equal("123 мама 456", result);
        Assert.Equal(LayoutConverter.Direction.ToRu, dir);
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
