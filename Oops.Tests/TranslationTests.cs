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

public class VoiceTests
{
    [Fact]
    public void VoiceModelHasATrustedUrlAndAChecksum()
    {
        var file = Assert.Single(ModelCatalog.Voice.Files);
        Assert.True(ModelStore.IsTrustedUrl(file.Url), file.Url);
        Assert.Equal(64, file.Sha256.Length);
        Assert.True(file.Sha256.All(Uri.IsHexDigit));
        Assert.False(file.Gzip);            // ggml-модель публикуется как есть
        Assert.True(file.Bytes > 100_000_000);
    }

    [Fact]
    public void WavHeaderDescribesWhatWhisperExpects()
    {
        // 16 кГц, моно, 16 бит — единственный формат, который принимает
        // whisper.cpp. Ошибка в заголовке даёт не отказ, а тишину на выходе.
        var wav = Recorder.WrapInWav(new byte[320]);

        Assert.Equal(44 + 320, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));                  // PCM
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));                  // моно
        Assert.Equal(Recorder.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));                 // бит на сэмпл
        Assert.Equal(320, BitConverter.ToInt32(wav, 40));                // длина данных
    }
}
