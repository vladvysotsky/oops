using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class TranslationTests
{
    public static TheoryData<ModelFile> AllFiles()
    {
        var data = new TheoryData<ModelFile>();
        foreach (var package in ModelCatalog.Translation)
            foreach (var file in package.Files) data.Add(file);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void EveryModelFileHasATrustedUrlAndAChecksum(ModelFile file)
    {
        // Опечатка в адресе или сумме превращает загрузку моделей в загрузку
        // чего угодно. Проверяем до запуска: качать десятки мегабайт, чтобы
        // узнать про опечатку, — плохая цена ошибки.
        Assert.True(ModelStore.IsTrustedUrl(file.Url), file.Url);
        Assert.Equal(64, file.Sha256.Length);
        Assert.True(file.Sha256.All(Uri.IsHexDigit), file.Sha256);
        Assert.True(file.Bytes > 0);
        Assert.EndsWith(".gz", file.Url);
        Assert.True(file.Gzip);
    }

    [Fact]
    public void EachTranslationPackageHasModelVocabAndShortlist()
    {
        // Конфиг движка собирается по префиксам имён: не хватит любого из трёх —
        // и BlockingService падает с «Failed to create translator instance».
        foreach (var package in ModelCatalog.Translation)
        {
            Assert.Contains(package.Files, f => f.Name.StartsWith("model."));
            Assert.Contains(package.Files, f => f.Name.StartsWith("vocab."));
            Assert.Contains(package.Files, f => f.Name.StartsWith("lex."));
        }
    }

    [Theory]
    [InlineData("привет, как дела", true)]
    [InlineData("hello there", false)]
    [InlineData("ёжик", true)]
    // Цифры и знаки голоса не имеют — иначе «2024 (!!!)» выбирало бы направление.
    [InlineData("2024 — 100%", false)]
    [InlineData("ok, всё хорошо и замечательно", true)]
    public void DirectionFollowsTheAlphabetOfTheText(string text, bool cyrillic) =>
        Assert.Equal(cyrillic, Translator.IsMostlyCyrillic(text));
}
