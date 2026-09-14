using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class ChangelogTests
{
    private static readonly Dictionary<string, string> Ru = L10n.Load(L10n.Russian);
    private static readonly Dictionary<string, string> En = L10n.Load(L10n.English);

    [Fact]
    public void EveryEntryHasBothTranslations()
    {
        // Пропущенный ключ виден как «news.2_1.replace.title» прямо в окне,
        // которое человек открывает сразу после обновления — худшее место.
        foreach (var set in Changelog.All)
            foreach (var entry in set.Entries)
                foreach (var key in new[] { entry.TitleKey, entry.BodyKey })
                {
                    Assert.True(Ru.ContainsKey(key), "нет в ru: " + key);
                    Assert.True(En.ContainsKey(key), "нет в en: " + key);
                }
    }

    [Fact]
    public void VersionsGoFromNewestToOldest()
    {
        // Порядок в списке — порядок на экране: свежее сверху.
        var versions = Changelog.All.Select(c => c.Version).ToList();
        Assert.Equal(versions.OrderByDescending(v => v).ToList(), versions);
    }

    [Fact]
    public void FreshInstallShowsNothing()
    {
        // Вывалить на нового человека историю версий — верный способ заставить
        // его закрыть окно не читая.
        Assert.Empty(Changelog.Since(null));
    }

    [Fact]
    public void OnlyVersionsNewerThanSeenAreShown()
    {
        var sets = Changelog.Since(new Version(2, 0, 1));
        Assert.All(sets, s => Assert.True(s.Version > new Version(2, 0, 1)));
        Assert.DoesNotContain(sets, s => s.Version == new Version(2, 0, 0));
    }

    [Fact]
    public void HistoryGoesBackToTheFirstRelease()
    {
        // Боковой список показывает все версии от первой: человек, поставивший
        // программу сегодня, должен видеть, из чего она выросла.
        Assert.Contains(Changelog.All, c => c.Version == new Version(1, 0, 0));
        Assert.All(Changelog.All, c => Assert.NotEmpty(c.Entries));
    }

    [Fact]
    public void NothingNewAfterTheLatestVersion()
    {
        var newest = Changelog.All.Max(c => c.Version)!;
        Assert.Empty(Changelog.Since(newest));
    }
}
