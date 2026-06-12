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
    public void Y_IsNotTreatedAsEnglishVowel_SoCyrillicLooseTranliterationsAreCaught()
    {
        // 'rjvvtyns' = 'комменты' набранное в EN. Содержит 'y' (мапится на 'н' в RU),
        // но 'y' больше НЕ считается английской гласной → fallback срабатывает,
        // плюс 'комменты' теперь в расширенном словаре.
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze("rjvvtyns"));
    }

    [Fact]
    public void Tot_IsCorrectlyDetectedThroughEYoNormalization()
    {
        // 'tot' → 'еще'. В словаре есть 'ещё', но не 'еще';
        // нормализация ё↔е должна это поймать.
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze("tot"));
    }

    [Fact]
    public void VowelDensity_DetectsRussianWordEvenWithEnVowelLetter()
    {
        // 'gjvtyztim' → 'поменяешь'. В EN-исходнике есть 'i' (формально EN-гласная),
        // но в RU-конвертации 4 гласных против 1 — явно русское слово.
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze("gjvtyztim"));
    }

    [Theory]
    [InlineData("ghbdrn")]   // привкт → опечатка от привет
    [InlineData("ghbdfn")]   // привфн → опечатка от привет (другая буква)
    [InlineData("ghbidtn")]  // лишняя буква
    public void TypoTolerance_FuzzyMatchCatchesSingleEditMistakes(string word)
    {
        // С допуском на одну ошибку конвертация должна определять "русское слово в EN".
        Assert.Equal(AutoDetector.Verdict.WasMeantRussian, AutoDetector.Analyze(word));
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
