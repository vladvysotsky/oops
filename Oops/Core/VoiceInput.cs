using System.Text;
using Whisper.net;

namespace Oops.Core;

/// <summary>
/// Голосовой ввод: распознавание речи движком whisper.cpp, целиком на машине.
///
/// Как и перевод, ничего никуда не отправляет. Записанный звук существует
/// только в памяти и только до конца распознавания.
///
/// Модель тяжёлая (полгигабайта), поэтому <see cref="WhisperFactory"/>
/// создаётся один раз и живёт до выхода: пересоздавать её на каждое нажатие
/// значило бы ждать несколько секунд перед каждой фразой.
/// </summary>
public static class VoiceInput
{
    private static readonly object Gate = new();
    private static WhisperFactory? _factory;

    /// <summary>Модель скачана.</summary>
    public static bool IsReady => ModelStore.IsInstalled(ModelCatalog.Voice);

    public static Task EnsureModelAsync(
        IProgress<ModelProgress>? progress = null, CancellationToken ct = default) =>
        ModelStore.EnsureAsync(ModelCatalog.Voice, progress, ct);

    public static void RemoveModel()
    {
        Unload();
        ModelStore.Remove(ModelCatalog.Voice);
    }

    /// <summary>
    /// Распознаёт WAV (16 кГц, моно, 16 бит — что отдаёт <see cref="Recorder"/>).
    /// Возвращает пустую строку, если речи не нашлось.
    ///
    /// Язык определяется автоматически: человек, который переключает раскладку
    /// по десять раз в час, говорит на обоих языках, и спрашивать его о языке
    /// перед каждой фразой — худшее, что можно сделать.
    /// </summary>
    public static async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        var factory = Factory();
        await using var processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        using var audio = new MemoryStream(wav);
        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(audio, ct).ConfigureAwait(false))
            text.Append(segment.Text);

        // Whisper отдаёт сегменты с ведущим пробелом — на стыке они складываются
        // в двойные, а перед первым словом пробел просто лишний.
        return text.ToString().Trim();
    }

    /// <summary>Выгружает модель из памяти — она занимает сотни мегабайт.</summary>
    public static void Unload()
    {
        lock (Gate)
        {
            _factory?.Dispose();
            _factory = null;
        }
    }

    private static WhisperFactory Factory()
    {
        lock (Gate)
        {
            if (_factory != null) return _factory;
            if (!IsReady) throw new InvalidOperationException(L10n.T("voice.err.noModel"));

            var path = ModelStore.PathTo(ModelCatalog.Voice, ModelCatalog.Voice.Files[0]);
            return _factory = WhisperFactory.FromPath(path);
        }
    }
}
