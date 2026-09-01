using BergamotTranslatorSharp;

namespace Oops.Core;

/// <summary>
/// Локальный перевод RU↔EN движком Bergamot — тем же, что переводит страницы
/// в Firefox.
///
/// Полностью на машине: текст не уходит в сеть ни при каких условиях. Это не
/// приятная мелочь, а условие, при котором функцию вообще можно включать в
/// программу, которая видит всё, что человек печатает.
///
/// В отличие от смены раскладки и регистра, перевод НЕ сохраняет длину и не
/// обратим — поэтому модели «расширяющейся области» здесь нет: переводится
/// либо вся набранная лента, либо выделение, одним шагом. Расширять область
/// по нажатиям было бы бессмысленно (второй шаг переводил бы уже переведённое),
/// а «угадывать» границу программа не умеет по устройству.
///
/// Направление выбирается по тексту: больше кириллицы — переводим на
/// английский, иначе на русский.
/// </summary>
public static class Translator
{
    private static readonly object Gate = new();
    private static BlockingService? _ruEn;
    private static BlockingService? _enRu;

    /// <summary>Обе модели уже скачаны.</summary>
    public static bool IsReady => ModelCatalog.Translation.All(ModelStore.IsInstalled);

    /// <summary>Докачивает недостающие файлы обеих моделей.</summary>
    public static async Task EnsureModelsAsync(
        IProgress<ModelProgress>? progress = null, CancellationToken ct = default)
    {
        foreach (var package in ModelCatalog.Translation)
            await ModelStore.EnsureAsync(package, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Удаляет модели перевода с диска.</summary>
    public static void RemoveModels()
    {
        Unload();
        foreach (var package in ModelCatalog.Translation) ModelStore.Remove(package);
    }

    /// <summary>
    /// Переводит текст. Вызывать НЕ из потока хука: первая загрузка модели
    /// занимает сотни миллисекунд, а хук Windows отключает по таймауту.
    /// </summary>
    public static string Translate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        bool toEnglish = IsMostlyCyrillic(text);
        var service = Service(toEnglish);
        return service.Translate(text);
    }

    /// <summary>
    /// Кириллицы больше, чем латиницы. Считаем только буквы: цифры, пробелы и
    /// знаки есть в обоих языках и голоса не имеют.
    /// </summary>
    public static bool IsMostlyCyrillic(string text)
    {
        int cyrillic = 0, latin = 0;
        foreach (var c in text)
        {
            if (c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c is 'ё' or 'Ё') cyrillic++;
            else if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z') latin++;
        }
        return cyrillic > latin;
    }

    /// <summary>Закрывает движки и освобождает память — модели живут десятки мегабайт.</summary>
    public static void Unload()
    {
        lock (Gate)
        {
            _ruEn?.Dispose(); _ruEn = null;
            _enRu?.Dispose(); _enRu = null;
        }
    }

    private static BlockingService Service(bool toEnglish)
    {
        lock (Gate)
        {
            if (toEnglish) return _ruEn ??= Create(ModelCatalog.RuEn);
            return _enRu ??= Create(ModelCatalog.EnRu);
        }
    }

    private static BlockingService Create(ModelPackage package)
    {
        if (!ModelStore.IsInstalled(package))
            throw new InvalidOperationException(L10n.T("translate.err.noModel"));

        var dir = ModelStore.DirectoryFor(package);
        var config = Path.Combine(dir, "config.yml");
        File.WriteAllText(config, ConfigFor(package));
        return new BlockingService(config);
    }

    /// <summary>
    /// Конфиг движка. Пишем сами при каждом создании, а не кладём в модель:
    /// в нём фигурируют имена файлов, и рассинхрон конфига с содержимым папки
    /// даёт «Failed to create translator instance» без объяснений.
    ///
    /// relative-paths: true — конфиг лежит рядом с файлами модели, и путь к
    /// папке с кириллицей в имени пользователя движку знать не нужно.
    /// gemm-precision — по имени файла: у моделей *.intgemm.alphas.bin это
    /// int8shiftAlphaAll, у остальных int8shiftAll. Ошибка здесь даёт мусор
    /// на выходе, а не отказ.
    /// </summary>
    private static string ConfigFor(ModelPackage package)
    {
        string Name(string prefix) => package.Files.First(f => f.Name.StartsWith(prefix)).Name;

        var model = Name("model.");
        var vocab = Name("vocab.");
        var shortlist = Name("lex.");
        var precision = model.Contains("alphas") ? "int8shiftAlphaAll" : "int8shiftAll";

        // vocabs указывается дважды: у моделей Mozilla словарь общий для
        // исходного и целевого языка, но движок ждёт ровно два элемента.
        return $"""
                relative-paths: true
                models:
                - {model}
                vocabs:
                - {vocab}
                - {vocab}
                shortlist:
                - {shortlist}
                - false
                beam-size: 1
                normalize: 1.0
                word-penalty: 0
                max-length-break: 128
                mini-batch-words: 1024
                workspace: 128
                max-length-factor: 2.0
                skip-cost: true
                cpu-threads: 0
                quiet: true
                quiet-translation: true
                gemm-precision: {precision}

                """;
    }
}
