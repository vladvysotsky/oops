using KeyLangSwitcher.Core;
using Xunit;

namespace KeyLangSwitcher.Tests;

public class AutoDetectorTests
{
    [Theory]
    [InlineData("ghbdtn")]       // привет — в RU-словаре
    [InlineData("vfvf")]         // мама — в RU-словаре
    public void DictionaryHit_OnConvertedLatin_TriggersRu(string word)
    {
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze(word));
    }

    [Theory]
    [InlineData("руддщ")]        // hello — в EN-словаре после layout-конвертации
    public void DictionaryHit_OnConvertedCyrillic_TriggersEn(string word)
    {
        Assert.Equal(AutoDetector.Verdict.WasMeantEnglish, AutoDetector.Analyze(word));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("привет")]
    [InlineData("the")]
    [InlineData("я")]
    public void KnownWord_InNativeLanguage_IsKept(string word)
    {
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
    }

    [Theory]
    [InlineData("XML")]
    [InlineData("PDF")]
    [InlineData("USB")]
    [InlineData("ФБР")]
    [InlineData("TCP")]
    public void AllUppercase_AreAlwaysKept(string word)
    {
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
    }

    [Theory]
    [InlineData("xml")]   // короткое без гласных — но < MinFallback (5)
    [InlineData("pdf")]
    [InlineData("tcp")]
    [InlineData("htm")]
    public void ShortUnknownWords_NoLongerFalsePositive(string word)
    {
        // Раньше vowel-fallback ловил на 3 символах — теперь порог 5.
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
    }

    [Fact]
    public void LongUnknownLatinWithoutVowels_TriggersFallback()
    {
        // 6 символов, не в словаре, нет EN-гласных → эвристика срабатывает.
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze("xczxsd"));
    }

    [Fact]
    public void ContextEnglish_SuppressesAmbiguousLatinFallback()
    {
        // Длинная латиница без гласных — обычно WasMeantRussian, но если
        // вокруг английский контекст, остаёмся в EN.
        var word = "xczxsd";
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian,
            AutoDetector.Analyze(word, AutoDetector.ContextLanguage.Unknown));
        Assert.Equal(AutoDetector.Verdict.Keep,
            AutoDetector.Analyze(word, AutoDetector.ContextLanguage.English));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("")]
    public void EmptyOrSingleChar_AreKept(string word)
    {
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
    }

    [Theory]
    [InlineData("helloпривет")]
    [InlineData("abc123")]
    [InlineData("hi!")]
    public void MixedScriptOrNonLetters_AreKept(string word)
    {
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
    }
}
