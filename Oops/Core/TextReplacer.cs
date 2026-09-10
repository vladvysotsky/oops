using System.Text.RegularExpressions;

namespace Oops.Core;

/// <summary>Что и на что менять, и как искать.</summary>
/// <param name="Find">Что ищем: подстрока или регулярное выражение.</param>
/// <param name="Replace">Чем заменяем. В режиме регулярок работают $1, $2…</param>
/// <param name="Regex">Искать регулярным выражением, а не подстрокой.</param>
/// <param name="CaseSensitive">Различать заглавные и строчные.</param>
/// <param name="WholeWord">Только целые слова.</param>
public sealed record ReplaceRule(
    string Find,
    string Replace,
    bool Regex = false,
    bool CaseSensitive = false,
    bool WholeWord = false);

/// <summary>Итог замены: текст, сколько раз сработало, и ошибка в выражении.</summary>
/// <param name="Text">Результат. При ошибке — исходный текст без изменений.</param>
/// <param name="Count">Сколько совпадений заменено.</param>
/// <param name="Error">Текст ошибки разбора выражения, иначе null.</param>
public readonly record struct ReplaceResult(string Text, int Count, string? Error)
{
    public bool Failed => Error != null;
}

/// <summary>
/// Замена в выделенном тексте — обычная и по регулярному выражению.
///
/// Выражение пишет пользователь, поэтому у поиска стоит таймаут: `(a+)+$` на
/// длинной строке уходит в перебор на часы, и без ограничения приложение
/// повисло бы целиком — вместе с клавиатурным хуком, то есть утащило бы за
/// собой ввод во всей системе.
///
/// Ошибка разбора не бросается наружу: неверное выражение — обычное состояние
/// поля, пока его дописывают, и падать на каждом втором символе нельзя.
/// </summary>
public static class TextReplacer
{
    /// <summary>
    /// Потолок времени на поиск. Секунда — заметная задержка, но всё ещё
    /// «подождал», а не «повисло».
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static ReplaceResult Apply(string input, ReplaceRule rule)
    {
        if (string.IsNullOrEmpty(rule.Find)) return new ReplaceResult(input, 0, null);

        try
        {
            var regex = Build(rule);
            int count = 0;
            var text = regex.Replace(input, m =>
            {
                count++;
                // В обычном режиме замена подставляется как есть: человек,
                // который ищет «$5», не имеет в виду пятую группу.
                return rule.Regex ? m.Result(rule.Replace) : rule.Replace;
            });
            return new ReplaceResult(text, count, null);
        }
        catch (ArgumentException ex)
        {
            // Неверное выражение или ссылка на несуществующую группу в замене.
            return new ReplaceResult(input, 0, ex.Message);
        }
        catch (RegexMatchTimeoutException)
        {
            return new ReplaceResult(input, 0, L10n.T("replace.err.timeout"));
        }
    }

    /// <summary>Проверка выражения для подсветки поля, без самой замены.</summary>
    public static string? Validate(ReplaceRule rule)
    {
        if (!rule.Regex || rule.Find.Length == 0) return null;
        try { _ = Build(rule); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }

    private static Regex Build(ReplaceRule rule)
    {
        // В обычном режиме экранируем всё: точка в «конец.» должна означать
        // точку, а не «любой символ».
        var pattern = rule.Regex ? rule.Find : Regex.Escape(rule.Find);

        // Границы слова ставим снаружи выражения, а не подмешиваем внутрь:
        // «\bfoo|bar\b» означает не то, что человек имеет в виду.
        if (rule.WholeWord) pattern = $@"\b(?:{pattern})\b";

        var options = RegexOptions.Multiline;
        if (!rule.CaseSensitive) options |= RegexOptions.IgnoreCase;
        return new Regex(pattern, options, Timeout);
    }

    /// <summary>
    /// Кирпичики для мастера: кусок выражения и человеческое название.
    /// Мастер собирает выражение из них, а не заставляет вспоминать синтаксис.
    /// </summary>
    public sealed record Block(string TitleKey, string Pattern);

    public static readonly IReadOnlyList<Block> Blocks = new[]
    {
        new Block("regex.block.digits",     @"\d+"),
        new Block("regex.block.letters",    @"\w+"),
        new Block("regex.block.spaces",     @"\s+"),
        new Block("regex.block.any",        @".+"),
        new Block("regex.block.lineStart",  "^"),
        new Block("regex.block.lineEnd",    "$"),
        new Block("regex.block.group",      "(...)"),
        new Block("regex.block.optional",   "?"),
        new Block("regex.block.either",     "|"),
    };

    /// <summary>
    /// Готовые правила: то, ради чего замену чаще всего и открывают.
    /// Названия — по задаче («убрать двойные пробелы»), а не по выражению.
    /// </summary>
    public sealed record Recipe(string TitleKey, ReplaceRule Rule);

    public static readonly IReadOnlyList<Recipe> Recipes = new[]
    {
        new Recipe("regex.recipe.doubleSpaces", new ReplaceRule(@"[ \t]{2,}", " ", Regex: true)),
        new Recipe("regex.recipe.trimLines",    new ReplaceRule(@"[ \t]+$", "", Regex: true)),
        new Recipe("regex.recipe.blankLines",   new ReplaceRule(@"(\r?\n){3,}", "\n\n", Regex: true)),
        new Recipe("regex.recipe.lineBreaks",   new ReplaceRule(@"\r?\n", " ", Regex: true)),
        new Recipe("regex.recipe.quotes",       new ReplaceRule("\"([^\"]*)\"", "«$1»", Regex: true)),
        new Recipe("regex.recipe.dashes",       new ReplaceRule(" - ", " — ")),
        new Recipe("regex.recipe.htmlTags",     new ReplaceRule("<[^>]+>", "", Regex: true)),
        new Recipe("regex.recipe.emails",       new ReplaceRule(@"[\w.+-]+@[\w-]+\.[\w.]+", "", Regex: true)),
    };
}
