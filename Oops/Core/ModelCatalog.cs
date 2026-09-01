namespace Oops.Core;

/// <summary>
/// Какие модели программа умеет скачивать и откуда.
///
/// Модели перевода — из официального реестра Mozilla (тот же движок и те же
/// файлы, что в переводчике Firefox): свободная лицензия, работают полностью
/// на машине пользователя, ничего никуда не отправляют.
///
/// Суммы SHA-256 сняты с ОПУБЛИКОВАННЫХ файлов (то есть с архивов .gz) и
/// зашиты здесь намеренно, а не читаются из реестра на лету: реестр приходит
/// с того же хоста, что и сами файлы, и подтверждать ими друг друга смысла
/// нет. Зашитая сумма привязывает выпуск программы к конкретным файлам —
/// подменить их по дороге уже нельзя.
///
/// Сверено при снятии: распакованное содержимое каждой модели совпадает с
/// uncompressedHash из реестра Mozilla.
/// </summary>
public static class ModelCatalog
{
    private const string MozBase =
        "https://storage.googleapis.com/moz-fx-translations-data--303e-prod-translations-data/";

    /// <summary>Русский → английский. Архитектура tiny, статус Release у Mozilla.</summary>
    public static readonly ModelPackage RuEn = new("translate-ru-en", new[]
    {
        Moz("models/ru-en/spring-2024_QrcdYgbwS7e7xbhtOSdoNQ/exported/",
            "model.ruen.intgemm.alphas.bin",
            "4a8a7b9b07c9e06a167ec5bf2542528817321516db4edf614fda45011fa8e5d1", 12_613_599),
        Moz("models/ru-en/spring-2024_QrcdYgbwS7e7xbhtOSdoNQ/exported/",
            "lex.50.50.ruen.s2t.bin",
            "6524f5c898f1fef52992bd2565a6d4acfafb6a4e8dcd6aef237bd888239418a0", 1_962_008),
        Moz("models/ru-en/spring-2024_QrcdYgbwS7e7xbhtOSdoNQ/exported/",
            "vocab.ruen.spm",
            "cd70b828e99e4d0c79d48cd56d8579d656c87c1db20bf88883da3085dcbfef75", 419_860),
    });

    /// <summary>Английский → русский. Архитектура base, статус Release Desktop у Mozilla.</summary>
    public static readonly ModelPackage EnRu = new("translate-en-ru", new[]
    {
        Moz("models/en-ru/student_base_AYqN3ysXRp2EGkEqeaA5Rg/exported/",
            "model.enru.intgemm.alphas.bin",
            "3c6e3ffd275c96a220ae28ddb55b8c2b86b44ffecc1eeb6c8195c1536de4ac74", 30_698_731),
        Moz("models/en-ru/student_base_AYqN3ysXRp2EGkEqeaA5Rg/exported/",
            "lex.50.50.enru.s2t.bin",
            "6f00f5d955b8b259cb4a78e5badf0958e5fef3ce153fd77a6dea0324518bb1b8", 1_348_215),
        Moz("models/en-ru/student_base_AYqN3ysXRp2EGkEqeaA5Rg/exported/",
            "vocab.enru.spm",
            "07ed9055319f2adc50a16bc7e636fe1547ab745eb986e40b6384956dc1fc6cfd", 419_005),
    });

    /// <summary>Оба направления перевода: без обоих переводить нечем.</summary>
    public static readonly IReadOnlyList<ModelPackage> Translation = new[] { RuEn, EnRu };

    /// <summary>Сколько всего качать для перевода, в мегабайтах (для текста в окне).</summary>
    public static int TranslationMegabytes =>
        (int)Math.Round(Translation.Sum(p => p.TotalBytes) / 1024.0 / 1024.0);

    /// <summary>
    /// Файл из реестра Mozilla. Имя задаётся БЕЗ .gz — оно же станет именем на
    /// диске после распаковки; скачивается архив, размер и сумма — от него.
    /// </summary>
    private static ModelFile Moz(string dir, string name, string sha256, long gzBytes) =>
        new(name, MozBase + dir + name + ".gz", sha256, gzBytes, Gzip: true);
}
