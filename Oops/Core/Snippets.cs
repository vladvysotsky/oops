namespace Oops.Core;

/// <summary>Сниппет: сокращение и то, во что оно раскрывается.</summary>
public sealed class Snippet
{
    public string Trigger { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Поиск сниппета по только что набранному слову.
///
/// Сокращение сравнивается БЕЗ УЧЁТА РАСКЛАДКИ И РЕГИСТРА: «!ee», «!уу» и «!EE» —
/// одно и то же, потому что это одни и те же клавиши. Иначе сниппет молчал бы
/// ровно тогда, когда раскладка перепутана, — а это основная работа программы.
///
/// Это не угадывание: сокращение человек задал сам, программа лишь узнаёт его.
/// </summary>
public sealed class SnippetMatcher
{
    private readonly LayoutPair _pair;
    private readonly Dictionary<string, string> _byKey = new();

    public SnippetMatcher(IEnumerable<Snippet> snippets, LayoutPair pair)
    {
        _pair = pair;
        foreach (var s in snippets)
        {
            if (!IsValidTrigger(s.Trigger) || string.IsNullOrEmpty(s.Text)) continue;
            // Два сниппета на одни и те же клавиши — побеждает первый: окно
            // редактирования такого не пропускает, а в файл могли вписать руками.
            _byKey.TryAdd(Key(s.Trigger, pair), s.Text);
        }
    }

    public static SnippetMatcher Empty { get; } = new(Array.Empty<Snippet>(), LayoutPair.BuiltIn);

    public bool IsEmpty => _byKey.Count == 0;

    /// <summary>
    /// Годится ли строка в сокращения. Пробелов внутри быть не может: раскрытие
    /// срабатывает на пробеле, и сокращение из двух слов не набралось бы никогда.
    /// </summary>
    public static bool IsValidTrigger(string? trigger)
        => !string.IsNullOrEmpty(trigger) && !trigger.Any(char.IsWhiteSpace);

    /// <summary>
    /// Каноническая форма — по клавишам, а не по буквам. Символ первой
    /// раскладки пары остаётся как есть, символ второй переводится на ту же
    /// клавишу первой, всё приводится к нижнему регистру. Символ, которого нет
    /// ни в одной из двух, сравнивается как есть.
    /// </summary>
    public static string Key(string text, LayoutPair pair)
    {
        var chars = new char[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!pair.First.Produces(c) && pair.Second.Produces(c))
                c = pair.Second.ConvertTo(pair.First, c.ToString())[0];
            chars[i] = char.ToLowerInvariant(c);
        }
        return new string(chars);
    }

    /// <summary>Сниппет для слова, или false.</summary>
    public bool TryMatch(string word, out string text)
    {
        text = string.Empty;
        if (word.Length == 0 || IsEmpty) return false;
        if (!_byKey.TryGetValue(Key(word, _pair), out var found)) return false;
        text = found;
        return true;
    }

    /// <summary>
    /// Последнее слово ленты — то, что набрано после последнего пробела. Если
    /// лента кончается пробелом, слова нет: человек уже закончил его.
    /// </summary>
    public static string LastWord(string buffer)
    {
        if (buffer.Length == 0 || char.IsWhiteSpace(buffer[^1])) return string.Empty;
        int start = buffer.Length;
        while (start > 0 && !char.IsWhiteSpace(buffer[start - 1])) start--;
        return buffer[start..];
    }
}
