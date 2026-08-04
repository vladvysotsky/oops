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
    string PageUrl);

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

    /// <summary>Версия текущей сборки.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub отклоняет запросы без User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(Repo, CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>
    /// Возвращает сведения о последнем релизе, либо null, если запрос не удался
    /// или релизов нет. Сетевые ошибки не пробрасываются: проверка обновлений
    /// не должна мешать работе приложения.
    /// </summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return null;
            if (!TryParseVersion(tag, out var version)) return null;

            var title = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty;
            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;

            return new ReleaseInfo(version, tag, title, notes, FindInstallerAsset(root), page);
        }
        catch
        {
            return null;
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

    /// <summary>Ищет среди файлов релиза инсталлятор (.exe, в имени "setup").</summary>
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

            if (name.Contains("setup", StringComparison.OrdinalIgnoreCase)) return url;
            firstExe ??= url;
        }
        return firstExe;
    }

    /// <summary>
    /// Скачивает инсталлятор во временную папку и возвращает путь к нему.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(
        ReleaseInfo release, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(release.InstallerUrl))
            throw new InvalidOperationException("В релизе нет файла инсталлятора.");

        var dir = Path.Combine(Path.GetTempPath(), "OopsUpdate");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"Oops-{release.TagName}-setup.exe");

        using var client = CreateClient();
        using var response = await client
            .GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(path);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total > 0) progress?.Report((int)(read * 100 / total));
        }
        return path;
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

    /// <summary>Открывает страницу релизов в браузере — запасной путь, если файла нет.</summary>
    public static void OpenReleasesPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }
}
