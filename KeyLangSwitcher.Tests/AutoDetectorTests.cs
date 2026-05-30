using KeyLangSwitcher.Core;
using Xunit;

namespace KeyLangSwitcher.Tests;

public class AutoDetectorTests
{
    [Theory]
    [InlineData("ghbdtn")]       // привет — RU набранное в EN
    [InlineData("vfvfxrf")]      // мамачка — нет EN-гласных
    [InlineData("ghjuhfvvf")]    // программа — нет гласных в EN-слое
    public void EnWordWithoutVowels_DetectedAsRussian(string word)
    {
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze(word));
    }

    [Theory]
    [InlineData("руддщ")]        // hello — RU-слой EN-слова, нет ru-гласных
    public void RuWordWithoutVowels_DetectedAsEnglish(string word)
    {
        Assert.Equal(AutoDetector.Verdict.WasMeantEnglish, AutoDetector.Analyze(word));
    }

    [Theory]
    [InlineData("hello")]        // нормальное EN-слово, есть гласные
    [InlineData("привет")]       // нормальное RU-слово, есть гласные
    [InlineData("abc")]          // только 1 гласная, но есть — keep
    public void RegularWordsAreKept(string word)
    {
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
    }

    [Fact]
    public void Limitation_AcronymWithoutVowels_FalsePositive()
    {
        // Документированное ограничение эвристики: "XML", "PDF", "BMW" и т.п. —
        // короткие аббревиатуры без гласных классифицируются как "набрано в EN
        // на месте RU". Мы фиксируем это поведение тестом, чтобы знать, если что-то
        // изменится при следующей итерации детектора.
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze("XML"));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("")]
    public void ShortWordsAreNeverFlagged(string word)
    {
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
    }

    [Theory]
    [InlineData("helloпривет")]   // смешанные алфавиты
    [InlineData("abc123")]         // с цифрами
    [InlineData("hi!")]            // с пунктуацией
    public void MixedScriptOrNonLetters_AreKept(string word)
    {
        Assert.Equal(AutoDetector.Verdict.Keep, AutoDetector.Analyze(word));
    }
}
