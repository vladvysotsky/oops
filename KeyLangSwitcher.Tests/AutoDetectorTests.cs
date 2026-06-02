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
    [InlineData("hello")]        // известно в EN
    [InlineData("привет")]       // известно в RU
    [InlineData("the")]
    [InlineData("я")]
    public void KnownWord_InNativeLanguage_IsKept(string word)
    {
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
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

    [Fact]
    public void UnknownWordWithoutVowels_FallsBackToVowelHeuristic()
    {
        // Слово которого нет ни в одном словаре, но без EN-гласных →
        // эвристика всё равно классифицирует как набранное в EN на месте RU.
        // "xczxsd" — гарантированно отсутствует в словаре.
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze("xczxsd"));
    }
}
