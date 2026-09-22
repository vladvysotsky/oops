using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class LanguageModelTests
{
    private static double Ru(string w) => LanguageModel.Implausibility(w, LanguageModel.Language.Russian);
    private static double En(string w) => LanguageModel.Implausibility(w, LanguageModel.Language.English);

    [Theory]
    // Настоящие слова выглядят правдоподобнее мусора из другой раскладки.
    [InlineData("привет", "зфыыцщкв")]
    [InlineData("работает", "вщслук")]
    [InlineData("нужен", "гыуыефеу")]
    public void RealRussianWordsScoreBetterThanLayoutGarbage(string real, string garbage)
        => Assert.True(Ru(real) < Ru(garbage), $"{real} vs {garbage}");

    [Theory]
    [InlineData("password", "lkz")]
    [InlineData("online", "xtuj")]
    [InlineData("hello", "ye")]
    public void RealEnglishWordsScoreBetterThanLayoutGarbage(string real, string garbage)
        => Assert.True(En(real) < En(garbage), $"{real} vs {garbage}");

    [Fact]
    public void UnknownButWellFormedWordsAreAccepted()
    {
        // Модель не словарь: имён нет ни в одном списке слов, но сложены они
        // из обычных для языка сочетаний — и должны считаться правдоподобными.
        Assert.True(En("anderson") < En("xtuj"));
        Assert.True(En("helsinki") < En("lkz"));
    }

    [Fact]
    public void DigitsAndPunctuationMakeAWordImplausibleInBothLanguages()
    {
        // И это правда: «ye;ty» с точкой с запятой посреди слова почти
        // наверняка набрано не в той раскладке.
        Assert.True(En("ye;ty") > En("yesterday"));
    }

    [Fact]
    public void ScoreDoesNotDependOnCaseOrYo()
    {
        Assert.Equal(Ru("привет"), Ru("ПРИВЕТ"), 6);
        Assert.Equal(Ru("ежик"), Ru("ёжик"), 6);
    }
}
