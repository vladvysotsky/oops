namespace Oops.Core;

/// <summary>Какой хоткей показать рядом с пунктом. Разрешается уже в окне.</summary>
public enum HotkeyRef { None, Convert, Case, Translate, Voice, Replace }

/// <summary>Один пункт «что нового»: что появилось и как этим пользоваться.</summary>
/// <param name="TitleKey">Ключ заголовка в словаре.</param>
/// <param name="BodyKey">Ключ описания — обязательно «как работает», а не только «что добавили».</param>
/// <param name="Hotkey">Показать рядом фактическое сочетание из настроек, если оно есть.</param>
public sealed record ChangeEntry(string TitleKey, string BodyKey, HotkeyRef Hotkey = HotkeyRef.None);

/// <summary>Изменения одной версии.</summary>
public sealed record ChangeSet(Version Version, IReadOnlyList<ChangeEntry> Entries);

/// <summary>
/// Что нового — по версиям, от свежих к старым.
///
/// Живёт в коде, а не тянется из релиза на GitHub: окно должно открываться
/// без сети и мгновенно, а тексты — переводиться вместе с остальным
/// интерфейсом. Заодно это единственное место, где про новую функцию написано
/// «как ей пользоваться», а не «что сделано».
///
/// Хоткей берётся из настроек, а не из умолчаний: человек мог его поменять, и
/// показывать ему чужое сочетание — худший способ объяснить новую функцию.
/// </summary>
public static class Changelog
{
    public static readonly IReadOnlyList<ChangeSet> All = new[]
    {
        new ChangeSet(new Version(2, 1, 0), new[]
        {
            new ChangeEntry("news.2_1.replace.title", "news.2_1.replace.body", HotkeyRef.Replace),
            new ChangeEntry("news.2_1.wizard.title", "news.2_1.wizard.body"),
        }),
        new ChangeSet(new Version(2, 0, 1), new[]
        {
            new ChangeEntry("news.2_0_1.theme.title", "news.2_0_1.theme.body"),
        }),
        new ChangeSet(new Version(2, 0, 0), new[]
        {
            new ChangeEntry("news.2_0.translate.title", "news.2_0.translate.body", HotkeyRef.Translate),
            new ChangeEntry("news.2_0.voice.title", "news.2_0.voice.body", HotkeyRef.Voice),
            new ChangeEntry("news.2_0.language.title", "news.2_0.language.body"),
            new ChangeEntry("news.2_0.models.title", "news.2_0.models.body"),
        }),
    };

    /// <summary>
    /// Что показать тому, кто обновился с <paramref name="since"/>.
    /// Пусто — показывать нечего, окно не открываем.
    /// </summary>
    public static IReadOnlyList<ChangeSet> Since(Version? since)
    {
        if (since == null) return Array.Empty<ChangeSet>();
        return All.Where(c => c.Version > since).ToList();
    }
}
