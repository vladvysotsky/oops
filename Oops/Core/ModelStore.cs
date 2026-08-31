using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Oops.Core;

/// <summary>Один файл модели в том виде, в каком он опубликован.</summary>
/// <param name="Name">Имя файла на диске. Без путей — только имя.</param>
/// <param name="Url">Откуда качать. Проверяется по списку разрешённых хостов.</param>
/// <param name="Sha256">
/// Сумма файла ИМЕННО В ТОМ ВИДЕ, В КАКОМ ОН СКАЧИВАЕТСЯ: для сжатых —
/// сумма архива, не распакованного содержимого. Иначе докачку пришлось бы
/// проверять по данным, которых на диске нет.
/// </param>
/// <param name="Bytes">Ожидаемый размер архива — только для показа прогресса.</param>
/// <param name="Gzip">Файл опубликован как .gz и распаковывается после сверки.</param>
public sealed record ModelFile(string Name, string Url, string Sha256, long Bytes, bool Gzip = false);

/// <summary>Набор файлов, который имеет смысл только целиком.</summary>
public sealed record ModelPackage(string Id, IReadOnlyList<ModelFile> Files)
{
    /// <summary>Сколько всего качать — для «нужно скачать 42 МБ» до начала.</summary>
    public long TotalBytes => Files.Sum(f => f.Bytes);
}

/// <summary>Ход загрузки: файл, его номер и общий процент по всему набору.</summary>
public readonly record struct ModelProgress(string FileName, int FileIndex, int FileCount, int Percent);

/// <summary>
/// Общее хранилище моделей для перевода и голосового ввода.
///
/// Модели весят десятки и сотни мегабайт — в инсталлятор их не положишь,
/// значит они приходят из сети уже после установки. Это второе место
/// программы (после автообновления), где внешние данные попадают на диск,
/// поэтому правила те же и ослаблять их нельзя:
///
///   - хост из списка разрешённых и только HTTPS (<see cref="IsTrustedUrl"/>);
///   - SHA-256 обязателен и сверяется ДО того, как файл встанет на своё имя;
///   - имя файла берётся из нашего описания пакета, а не из ответа сервера
///     (иначе Content-Disposition или редирект решали бы, куда писать);
///   - потолок размера, чтобы бесконечный поток не забил диск.
///
/// Модель — не исполняемый код, но её читает нативная библиотека, а разбор
/// битого или подложенного файла в нативном коде — обычный способ получить
/// исполнение. Сверка суммы здесь не формальность.
///
/// Загрузка возобновляемая: файл пишется в <c>.part</c> рядом, и при обрыве
/// следующая попытка досылает остаток через Range. Сто мегабайт по слабому
/// каналу иначе не доезжают никогда.
/// </summary>
public static class ModelStore
{
    /// <summary>Куда складываем. Рядом с настройками, не в Program Files.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oops", "models");

    private const long MaxFileBytes = 2L * 1024 * 1024 * 1024;

    public static string DirectoryFor(ModelPackage package) => Path.Combine(Root, package.Id);

    public static string PathTo(ModelPackage package, ModelFile file) =>
        Path.Combine(DirectoryFor(package), SafeName(file.Name));

    /// <summary>Все ли файлы набора уже лежат на диске.</summary>
    public static bool IsInstalled(ModelPackage package) =>
        package.Files.All(f => File.Exists(PathTo(package, f)));

    /// <summary>Сколько ещё осталось скачать — недостающие файлы, в байтах.</summary>
    public static long MissingBytes(ModelPackage package) =>
        package.Files.Where(f => !File.Exists(PathTo(package, f))).Sum(f => f.Bytes);

    /// <summary>
    /// Докачивает недостающие файлы набора. Уже лежащие на диске не трогает:
    /// повторная сверка сотен мегабайт при каждом запуске стоит секунд, а
    /// подменить файл в профиле пользователя может только тот, кто и так уже
    /// работает от его имени.
    /// </summary>
    public static async Task EnsureAsync(ModelPackage package,
        IProgress<ModelProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(DirectoryFor(package));

        for (int i = 0; i < package.Files.Count; i++)
        {
            var file = package.Files[i];
            var target = PathTo(package, file);
            if (File.Exists(target)) continue;

            var slot = new ModelProgress(file.Name, i + 1, package.Files.Count, 0);
            progress?.Report(slot);

            await FetchAsync(file, target,
                new Progress<int>(p => progress?.Report(slot with { Percent = p })), ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Удаляет набор целиком — освободить место из настроек.</summary>
    public static void Remove(ModelPackage package)
    {
        var dir = DirectoryFor(package);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ------------------------------------------------------------ внутреннее

    private static async Task FetchAsync(ModelFile file, string target,
        IProgress<int>? progress, CancellationToken ct)
    {
        if (!IsTrustedUrl(file.Url))
            throw new InvalidOperationException(L10n.T("model.err.untrustedUrl", file.Name));
        if (file.Sha256.Length != 64)
            throw new InvalidOperationException(L10n.T("model.err.noChecksum", file.Name));

        var part = target + ".part";
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;

        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        // Сервер вправе не поддержать докачку и отдать файл целиком — тогда
        // начинаем заново, иначе к остатку припишется его же начало.
        if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            have = 0;
            File.Delete(part);
        }
        response.EnsureSuccessStatusCode();

        long total = have + (response.Content.Headers.ContentLength ?? file.Bytes);
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var sink = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long written = have;
            int n;
            while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                written += n;
                if (written > MaxFileBytes)
                    throw new InvalidOperationException(L10n.T("model.err.tooBig", file.Name));
                await sink.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                if (total > 0) progress?.Report((int)Math.Min(100, written * 100 / total));
            }
        }

        // Сверяем ДО распаковки и до того, как файл получит своё настоящее имя:
        // на .part никто не смотрит, а распаковка битого gzip — уже разбор
        // недоверенных данных.
        var actual = await HashAsync(part, ct).ConfigureAwait(false);
        if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidOperationException(L10n.T("model.err.checksumMismatch", file.Name));
        }

        if (file.Gzip) Decompress(part, target);
        else File.Move(part, target, overwrite: true);
    }

    private static void Decompress(string part, string target)
    {
        var raw = target + ".raw";
        using (var packed = File.OpenRead(part))
        using (var gz = new GZipStream(packed, CompressionMode.Decompress))
        using (var plain = File.Create(raw))
            gz.CopyTo(plain);

        // Имя появляется одним движением: наполовину распакованный файл под
        // настоящим именем сошёл бы за установленный и упал бы при чтении.
        File.Move(raw, target, overwrite: true);
        File.Delete(part);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await sha.ComputeHashAsync(file, ct).ConfigureAwait(false));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("oops", UpdateService.CurrentVersion.ToString()));
        return client;
    }

    /// <summary>
    /// Только HTTPS и только хосты, где лежат модели. Список закрытый: подставить
    /// свой адрес в описание пакета — самый дешёвый способ заставить программу
    /// скачать что угодно.
    ///
    /// Поддомены разрешены точечно, по суффиксу: файлы Hugging Face отдаются
    /// с cdn-lfs*.hf.co, и редирект на них — обычное дело.
    /// </summary>
    public static bool IsTrustedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps) return false;

        var host = u.Host;
        return Matches(host, "huggingface.co")
            || Matches(host, "hf.co")
            || Matches(host, "storage.googleapis.com")
            || Matches(host, "github.com")
            || Matches(host, "githubusercontent.com");

        static bool Matches(string host, string domain) =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Имя файла — только имя. «..\..\что-нибудь.exe» в описании пакета не
    /// должно вывести запись за пределы папки моделей.
    /// </summary>
    private static string SafeName(string name)
    {
        var bare = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(bare) || bare is "." or "..")
            throw new InvalidOperationException(L10n.T("model.err.badName", name));
        return bare;
    }
}
