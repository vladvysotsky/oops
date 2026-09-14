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
        new ChangeSet(new Version(1, 3, 0), new[]
        {
            new ChangeEntry("news.1_3.feedback.title", "news.1_3.feedback.body"),
            new ChangeEntry("news.1_3.layout.title", "news.1_3.layout.body"),
        }),
        new ChangeSet(new Version(1, 2, 3), new[]
        {
            new ChangeEntry("news.1_2_3.notes.title", "news.1_2_3.notes.body"),
        }),
        new ChangeSet(new Version(1, 2, 2), new[]
        {
            new ChangeEntry("news.1_2_2.hotkeyField.title", "news.1_2_2.hotkeyField.body"),
        }),
        new ChangeSet(new Version(1, 2, 1), new[]
        {
            new ChangeEntry("news.1_2_1.releaseNotes.title", "news.1_2_1.releaseNotes.body"),
        }),
        new ChangeSet(new Version(1, 2, 0), new[]
        {
            new ChangeEntry("news.1_2.fastTyping.title", "news.1_2.fastTyping.body"),
            new ChangeEntry("news.1_2.errors.title", "news.1_2.errors.body"),
            new ChangeEntry("news.1_2.checksum.title", "news.1_2.checksum.body"),
        }),
        new ChangeSet(new Version(1, 1, 0), new[]
        {
            new ChangeEntry("news.1_1.welcome.title", "news.1_1.welcome.body"),
            new ChangeEntry("news.1_1.darkTheme.title", "news.1_1.darkTheme.body"),
            new ChangeEntry("news.1_1.caseHotkey.title", "news.1_1.caseHotkey.body", HotkeyRef.Case),
        }),
        new ChangeSet(new Version(1, 0, 0), new[]
        {
            new ChangeEntry("news.1_0.layout.title", "news.1_0.layout.body", HotkeyRef.Convert),
            new ChangeEntry("news.1_0.case.title", "news.1_0.case.body", HotkeyRef.Case),
            new ChangeEntry("news.1_0.selection.title", "news.1_0.selection.body"),
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
