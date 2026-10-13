using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class KeyboardLayoutsTests
{
    private static KeyboardLayout Layout(string id, string language, params (ushort Key, char Char)[] keys)
        => new(id, language, id, IntPtr.Zero,
            keys.Select(k => (new KeyStroke(k.Key, false), k.Char)));

    // Маленькие условные раскладки: «клавиша» — просто номер. Украинская
    // взята намеренно: для неё модели языка нет, и конвертер обязан работать
    // и без неё.
    private static readonly KeyboardLayout En = Layout("en", "en",
        (1, 'g'), (2, 'h'), (3, 'b'), (4, 'd'), (5, 's'), (6, 'n'));
    private static readonly KeyboardLayout Uk = Layout("uk", "uk",
        (1, 'п'), (2, 'р'), (3, 'и'), (4, 'в'), (5, 'і'), (6, 'т'));

    [Fact]
    public void BuiltInPairConvertsExactlyLikeTheOldTable()
    {
        // Встроенная пара собирается из той же таблицы, что и ToRussian/ToEnglish.
        // Разойдись они — все тесты конвертера проверяли бы не то, что работает.
        var pair = LayoutPair.BuiltIn;
        foreach (var (en, ru) in LayoutConverter.PairsLower.Concat(LayoutConverter.PairsUpper))
        {
            Assert.Equal(LayoutConverter.ToRussian(en.ToString()), pair.First.ConvertTo(pair.Second, en.ToString()));
            Assert.Equal(LayoutConverter.ToEnglish(ru.ToString()), pair.Second.ConvertTo(pair.First, ru.ToString()));
        }
    }

    [Fact]
    public void ConversionKeepsLengthAndLeavesUnknownCharactersAlone()
    {
        // На сохранении длины держится вся арифметика стирания.
        var text = "ghbdsn 42 ы!";
        var converted = En.ConvertTo(Uk, text);
        Assert.Equal(text.Length, converted.Length);
        Assert.Equal("привіт 42 ы!", converted);
    }

    [Fact]
    public void AnyPairWorksEvenWithoutALanguageModel()
    {
        // Модели украинского нет — судить «настоящее ли слово» нечем, и
        // конвертер переводит, как переводил всё до появления модели. Молчать
        // здесь значило бы, что сочетание на такой паре не работает вовсе.
        try
        {
            LayoutConverter.Pair = new LayoutPair(En, Uk);

            var (toUk, target) = LayoutConverter.Convert("ghbdsn");
            Assert.Equal("привіт", toUk);
            Assert.Same(Uk, target);

            var (back, backTarget) = LayoutConverter.Convert("привіт");
            Assert.Equal("ghbdsn", back);
            Assert.Same(En, backTarget);

            var (digits, none) = LayoutConverter.Convert("2024 !!!");
            Assert.Equal("2024 !!!", digits);
            Assert.Null(none);
        }
        finally
        {
            LayoutConverter.Pair = LayoutPair.BuiltIn;
        }
    }

    private static readonly KeyboardLayout SysUk = Layout("0x04220422", "uk");
    private static readonly KeyboardLayout SysEn = Layout("0x04090409", "en");
    private static readonly KeyboardLayout SysRu = Layout("0x04190419", "ru");

    [Fact]
    public void DefaultPairIsEnglishAndRussianWhenBothAreInstalled()
    {
        var pair = LayoutPair.Choose(new[] { SysUk, SysEn, SysRu }, "", "")!;
        Assert.Same(SysEn, pair.First);
        Assert.Same(SysRu, pair.Second);
    }

    [Fact]
    public void ChosenPairWinsOverTheDefault()
    {
        var pair = LayoutPair.Choose(new[] { SysUk, SysEn, SysRu }, SysUk.Id, SysEn.Id)!;
        Assert.Same(SysUk, pair.First);
        Assert.Same(SysEn, pair.Second);
    }

    [Theory]
    // Раскладку удалили из Windows после того, как её выбрали.
    [InlineData("0x04150415", "0x04090409")]
    // Одна и та же раскладка дважды — переводить не между чем.
    [InlineData("0x04090409", "0x04090409")]
    public void BrokenChoiceFallsBackToTheDefault(string first, string second)
    {
        var pair = LayoutPair.Choose(new[] { SysUk, SysEn, SysRu }, first, second)!;
        Assert.Same(SysEn, pair.First);
        Assert.Same(SysRu, pair.Second);
    }

    [Fact]
    public void WithoutEnglishOrRussianTheFirstTwoAreTaken()
    {
        var de = Layout("0x04070407", "de");
        var fr = Layout("0x040C040C", "fr");
        var pair = LayoutPair.Choose(new[] { de, fr }, null, null)!;
        Assert.Same(de, pair.First);
        Assert.Same(fr, pair.Second);
    }

    [Fact]
    public void OneLayoutIsNoPair()
        => Assert.Null(LayoutPair.Choose(new[] { SysEn }, null, null));
}
