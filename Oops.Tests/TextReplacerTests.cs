using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class TextReplacerTests
{
    private static ReplaceResult Run(string input, string find, string replace,
        bool regex = false, bool caseSensitive = false, bool wholeWord = false) =>
        TextReplacer.Apply(input, new ReplaceRule(find, replace, regex, caseSensitive, wholeWord));

    [Fact]
    public void PlainSearchTreatsThePatternLiterally()
    {
        // Точка в обычном режиме — точка. Иначе «конец.» съело бы «конецX».
        var r = Run("конец. начало", ".", "!");
        Assert.Equal("конец! начало", r.Text);
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void PlainReplacementIsLiteralToo()
    {
        // «$1» без регулярок — это доллар и единица, а не ссылка на группу.
        var r = Run("цена", "цена", "$1");
        Assert.Equal("$1", r.Text);
    }

    [Fact]
    public void RegexGroupsAreAvailableInTheReplacement()
    {
        var r = Run("\"слово\"", "\"([^\"]*)\"", "«$1»", regex: true);
        Assert.Equal("«слово»", r.Text);
    }

    [Fact]
    public void CaseIsIgnoredUnlessAsked()
    {
        Assert.Equal("X x", Run("А x", "а", "X").Text);
        Assert.Equal("А x", Run("А x", "а", "X", caseSensitive: true).Text);
    }

    [Fact]
    public void WholeWordDoesNotSplitTheAlternation()
    {
        // Границы слова ставятся вокруг ВСЕГО выражения: «\bкот|пёс\b» значило
        // бы «начало слова кот» ИЛИ «пёс в конце слова» — не то, что просили.
        var r = Run("кот и пёс, котлета", "кот|пёс", "зверь", regex: true, wholeWord: true);
        Assert.Equal("зверь и зверь, котлета", r.Text);
    }

    [Fact]
    public void BrokenExpressionReportsInsteadOfThrowing()
    {
        // Незакрытая скобка — обычное состояние поля, пока его дописывают.
        var r = Run("текст", "(", "", regex: true);
        Assert.True(r.Failed);
        Assert.Equal("текст", r.Text);      // исходный текст не тронут
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void EmptySearchChangesNothing()
    {
        var r = Run("текст", "", "что-нибудь");
        Assert.Equal("текст", r.Text);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void CountReportsHowManyTimesItFired()
    {
        var r = Run("а а а", "а", "б");
        Assert.Equal(3, r.Count);
    }

    [Fact]
    public void EveryRecipeIsAValidExpression()
    {
        // Опечатка в готовом правиле — это ошибка, которую человек примет на
        // свой счёт: он-то ничего не писал.
        foreach (var recipe in TextReplacer.Recipes)
            Assert.Null(TextReplacer.Validate(recipe.Rule));
    }

    [Fact]
    public void LineStartAnchorWorksPerLine()
    {
        // Multiline: «^» — начало каждой строки, а не всего куска. Замена в
        // выделенном почти всегда многострочная.
        var r = Run("раз\nдва", "^", "> ", regex: true);
        Assert.Equal("> раз\n> два", r.Text);
    }
}
