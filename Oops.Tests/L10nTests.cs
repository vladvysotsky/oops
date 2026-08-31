using System.Linq;
using System.Text.RegularExpressions;
using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class L10nTests
{
    // Собрать я не могу — эти тесты и есть проверка локализации до запуска.
    private static readonly Dictionary<string, string> Ru = L10n.Load(L10n.Russian);
    private static readonly Dictionary<string, string> En = L10n.Load(L10n.English);

    [Fact]
    public void BothLanguagesHaveTheSameKeys()
    {
        var onlyRu = Ru.Keys.Except(En.Keys).OrderBy(k => k).ToArray();
        var onlyEn = En.Keys.Except(Ru.Keys).OrderBy(k => k).ToArray();

        Assert.True(onlyRu.Length == 0, "нет английского перевода: " + string.Join(", ", onlyRu));
        Assert.True(onlyEn.Length == 0, "нет русского оригинала: " + string.Join(", ", onlyEn));
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        // Если в переводе потерялся {0}, подстановка молча исчезнет с экрана:
        // человек увидит «Установлена » без версии.
        foreach (var (key, ru) in Ru)
        {
            var expected = Placeholders(ru);
            var actual = Placeholders(En[key]);
            Assert.True(expected.SetEquals(actual),
                $"{key}: плейсхолдеры расходятся — ru [{string.Join(",", expected.Order())}], "
                + $"en [{string.Join(",", actual.Order())}]");
        }
    }

    [Fact]
    public void NoValueIsEmpty()
    {
        foreach (var (key, value) in Ru) Assert.False(string.IsNullOrWhiteSpace(value), key);
        foreach (var (key, value) in En) Assert.False(string.IsNullOrWhiteSpace(value), key);
    }

    [Fact]
    public void UnknownKeyReturnsItself()
    {
        // Осознанное поведение: пропущенный ключ виден на экране, но не роняет окно.
        L10n.Init(L10n.Russian);
        Assert.Equal("no.such.key", L10n.T("no.such.key"));
    }

    [Fact]
    public void AutoFollowsWindowsLanguage()
    {
        Assert.Equal(L10n.Russian, L10n.Resolve(L10n.Russian));
        Assert.Equal(L10n.English, L10n.Resolve(L10n.English));
        // «auto» и мусор из старого файла настроек разрешаются по языку системы.
        Assert.Contains(L10n.Resolve(L10n.Auto), new[] { L10n.Russian, L10n.English });
        Assert.Contains(L10n.Resolve("klingon"), new[] { L10n.Russian, L10n.English });
    }

    private static HashSet<string> Placeholders(string s) =>
        Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).ToHashSet();
}
