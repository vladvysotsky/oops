using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Oops.Core;

/// <summary>Сведения о доступном релизе на GitHub.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string TagName,
    string Title,
    string Notes,
    string? InstallerUrl,
    string PageUrl,
    string? ChecksumsUrl = null);

/// <summary>
/// Проверка и установка обновлений через GitHub Releases.
///
/// Берём последний релиз через публичный API, сравниваем его версию с версией
/// текущей сборки. Если новее — скачиваем приложенный инсталлятор и запускаем
/// его, после чего выходим, чтобы он мог заменить файлы.
///
/// Запросы анонимные: лимит GitHub — 60 обращений в час на IP, чего с запасом
/// хватает при проверке раз в сутки. Токен намеренно не используется, чтобы
/// не хранить секрет в клиентском приложении.
/// </summary>
public static class UpdateService
{
    // ВАЖНО: должно совпадать с именем репозитория на GitHub. После переименования
    // репозитория GitHub держит редирект со старого имени, но полагаться на него
    // не стоит — проверка обновлений молча перестанет находить релизы.
    private const string Owner = "vladvysotsky";
    private const string Repo = "oops";
    private static readonly Uri LatestReleaseApi =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repo}/releases";

    /// <summary>
    /// Ссылка на новый issue с уже заполненными заголовком и телом. Пересказывать
    /// ошибку своими словами человек не должен — он её и не понял.
    /// </summary>
    public static string NewIssueUrl(string title, string body, string? labels = null) =>
        $"https://github.com/{Owner}/{Repo}/issues/new"
        + $"?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}"
        + (labels != null ? $"&labels={Uri.EscapeDataString(labels)}" : "");

    /// <summary>Версия текущей сборки.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
            // Ответ API читается в память целиком; без потолка подменённый или
            // сломанный сервер мог бы отдавать бесконечный поток. На скачивание
            // инсталлятора не влияет — там ResponseHeadersRead и своя копия в файл.
            MaxResponseContentBufferSize = 4 * 1024 * 1024,
        };
        // GitHub отклоняет запросы без User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(Repo, CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>
    /// Итог проверки. «Релизов ещё нет» и «не удалось спросить» — разные вещи:
    /// в первом случае сообщать о проблемах с интернетом попросту неверно.
    /// </summary>
    /// <param name="Release">Последний релиз, если он есть.</param>
    /// <param name="Failed">Запрос не удался: нет сети, лимит, ошибка сервера.</param>
    /// <param name="Unavailable">
    /// Репозитория для нас не существует: он закрытый (private), переименован
    /// или удалён. Отличать обязательно — на анонимный запрос к приватному
    /// репозиторию GitHub отвечает 404, а не 403: он скрывает сам факт
    /// существования. Без этой проверки закрытый репозиторий выглядел как
    /// «релизов пока нет», и причину было не угадать.
    /// </param>
    public readonly record struct CheckResult(ReleaseInfo? Release, bool Failed, bool Unavailable = false)
    {
        /// <summary>Запрос прошёл, но опубликованных релизов в репозитории нет.</summary>
        public bool NoReleases => !Failed && !Unavailable && Release is null;
    }

    /// <summary>
    /// Спрашивает GitHub о последнем релизе. Исключения наружу не выпускает:
    /// проверка обновлений не должна мешать работе приложения.
    /// </summary>
    public static async Task<CheckResult> FetchLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);

            // 404 значит одно из двух: релизов ещё нет ИЛИ репозиторий нам не
            // виден. Различаем вторым запросом — иначе закрытый репозиторий
            // сообщает «релизов нет», и человек ищет причину не там.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                using var probe = await client
                    .GetAsync(new Uri($"https://api.github.com/repos/{Owner}/{Repo}"), ct)
                    .ConfigureAwait(false);
                bool unavailable = probe.StatusCode == System.Net.HttpStatusCode.NotFound;
                return new CheckResult(null, Failed: false, Unavailable: unavailable);
            }

            if (!response.IsSuccessStatusCode) return new CheckResult(null, Failed: true);

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return new CheckResult(null, Failed: true);
            if (!TryParseVersion(tag, out var version)) return new CheckResult(null, Failed: true);

            var title = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty;
            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;

            var release = new ReleaseInfo(version, tag, title, notes,
                FindInstallerAsset(root), page, FindChecksumsAsset(root));
            return new CheckResult(release, Failed: false);
        }
        catch
        {
            return new CheckResult(null, Failed: true);
        }
    }

    /// <summary>Есть ли релиз новее текущей сборки.</summary>
    public static bool IsNewer(ReleaseInfo release) => release.Version > CurrentVersion;

    /// <summary>
    /// Разбирает версию из тега релиза. Понимает "v1.2.3", "1.2.3" и "1.2.3.4";
    /// суффиксы вида "-beta" отбрасываются.
    /// </summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0);
        var s = tag.Trim().TrimStart('v', 'V');

        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];

        return Version.TryParse(s, out version!) && version != null;
    }

    /// <summary>
    /// Ищет среди файлов релиза инсталлятор (.exe, в имени "setup").
    ///
    /// URL принимается ТОЛЬКО с github.com: скачанный файл мы запускаем, и
    /// это единственное место программы, где данные из сети превращаются в
    /// исполняемый код. API отдаёт ссылки на github.com и без нас, так что
    /// проверка ничего не ломает — она отсекает сценарий, в котором ответ
    /// API подменён (прокси с подложенным корневым сертификатом, компромисс
    /// аккаунта с релизом, указывающим на чужой хост).
    /// </summary>
    private static string? FindInstallerAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? firstExe = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name == null || url == null) continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsTrustedDownloadUrl(url)) continue;

            if (name.Contains("setup", StringComparison.OrdinalIgnoreCase)) return url;
            firstExe ??= url;
        }
        return firstExe;
    }

    /// <summary>Ищет файл контрольных сумм SHA256SUMS.txt, который публикует CI.</summary>
    private static string? FindChecksumsAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name == null || url == null) continue;
            if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)
                && IsTrustedDownloadUrl(url))
                return url;
        }
        return null;
    }

    /// <summary>Только HTTPS и только github.com (файлы релизов живут именно там).</summary>
    private static bool IsTrustedDownloadUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || u.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || u.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Скачивает инсталлятор во временную папку, сверяет SHA-256 с
    /// опубликованным CI файлом SHA256SUMS.txt и возвращает путь.
    ///
    /// Сверка — не формальность: файл будет ЗАПУЩЕН. Битая докачка или
    /// подменённый по дороге файл должны умереть здесь, а не исполниться.
    /// Если файла сумм в релизе нет (старые релизы), скачиваем без сверки —
    /// иначе обновление сломалось бы у всех существующих пользователей.
    ///
    /// Имя файла содержит случайный суффикс: путь во временной папке
    /// предсказуем, и локальный вредонос мог бы подложить свой exe на
    /// известное имя до того, как мы его запустим.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(
        ReleaseInfo release, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(release.InstallerUrl))
            throw new InvalidOperationException("В релизе нет файла инсталлятора.");

        var dir = Path.Combine(Path.GetTempPath(), "OopsUpdate");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir,
            $"Oops-{release.TagName}-setup-{Guid.NewGuid():N}.exe");

        using var client = CreateClient();
        using var response = await client
            .GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var target = File.Create(path))
        {
            // Потолок размера: сверка суммы отбраковала бы бесконечный поток и
            // так, но только после того, как он забьёт диск.
            const long MaxInstallerBytes = 500L * 1024 * 1024;

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                read += n;
                if (read > MaxInstallerBytes)
                    throw new InvalidOperationException("Файл установщика подозрительно велик.");
                await target.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                if (total > 0) progress?.Report((int)(read * 100 / total));
            }
        }

        await VerifyChecksumAsync(release, path, ct).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// Сверяет SHA-256 скачанного файла с SHA256SUMS.txt из того же релиза.
    /// Формат строк — как у sha256sum: «хеш  имя-файла».
    /// </summary>
    private static async Task VerifyChecksumAsync(ReleaseInfo release, string path, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(release.ChecksumsUrl)) return;

        using var client = CreateClient();
        string sums = await client.GetStringAsync(release.ChecksumsUrl, ct).ConfigureAwait(false);

        // Оригинальное имя файла в релизе — без нашего случайного суффикса.
        var expected = sums
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[1].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0])
            .FirstOrDefault();

        // Сам список сумм пришёл по тому же каналу, что и файл, поэтому от
        // компрометации GitHub он не защищает — только от битой загрузки и
        // подмены в пути. Но отсутствие суммы .exe в файле, который CI всегда
        // пишет, — уже признак манипуляции с релизом.
        if (expected == null)
            throw new InvalidOperationException(
                "В SHA256SUMS.txt релиза нет суммы для установщика.");

        using var sha = System.Security.Cryptography.SHA256.Create();
        await using var file = File.OpenRead(path);
        var actual = Convert.ToHexString(await sha.ComputeHashAsync(file, ct).ConfigureAwait(false));

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException(
                "Контрольная сумма установщика не совпала с опубликованной. "
                + "Файл повреждён при загрузке или подменён — запускать его нельзя.");
        }
    }

    /// <summary>
    /// Запускает скачанный инсталлятор. Он сам закроет работающую копию
    /// (в скрипте Inno Setup стоит CloseApplications) и переустановит файлы.
    /// </summary>
    public static void LaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Открывает страницу релизов в браузере — запасной путь, если файла нет.
    ///
    /// URL приходит из ответа API (html_url), а Process.Start с UseShellExecute
    /// запускает ЧТО УГОДНО, не только браузер: file://, UNC-путь, локальный
    /// exe. Поэтому открываем только https — всё остальное заменяем нашей
    /// собственной страницей релизов.
    /// </summary>
    public static void OpenReleasesPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            url = ReleasesPageUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }
}
