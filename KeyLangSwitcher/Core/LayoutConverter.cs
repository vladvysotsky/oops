using System.Text;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Двунаправленная конвертация символов между QWERTY (en-US) и ЙЦУКЕН (ru).
/// Карта построена по позициям клавиш на стандартной 104-клавишной раскладке.
/// </summary>
public static class LayoutConverter
{
    // Пары: символ EN <-> символ RU в одной и той же физической позиции клавиши.
    // Нижний регистр.
    private static readonly (char en, char ru)[] PairsLower =
    {
        ('q','й'), ('w','ц'), ('e','у'), ('r','к'), ('t','е'), ('y','н'),
        ('u','г'), ('i','ш'), ('o','щ'), ('p','з'), ('[','х'), (']','ъ'),
        ('a','ф'), ('s','ы'), ('d','в'), ('f','а'), ('g','п'), ('h','р'),
        ('j','о'), ('k','л'), ('l','д'), (';','ж'), ('\'','э'),
        ('z','я'), ('x','ч'), ('c','с'), ('v','м'), ('b','и'), ('n','т'),
        ('m','ь'), (',','б'), ('.','ю'), ('/','.'),
        ('`','ё'),
    };

    // Верхний регистр / Shift-варианты.
    private static readonly (char en, char ru)[] PairsUpper =
    {
        ('Q','Й'), ('W','Ц'), ('E','У'), ('R','К'), ('T','Е'), ('Y','Н'),
        ('U','Г'), ('I','Ш'), ('O','Щ'), ('P','З'), ('{','Х'), ('}','Ъ'),
        ('A','Ф'), ('S','Ы'), ('D','В'), ('F','А'), ('G','П'), ('H','Р'),
        ('J','О'), ('K','Л'), ('L','Д'), (':','Ж'), ('"','Э'),
        ('Z','Я'), ('X','Ч'), ('C','С'), ('V','М'), ('B','И'), ('N','Т'),
        ('M','Ь'), ('<','Б'), ('>','Ю'), ('?',','),
        ('~','Ё'),
    };

    private static readonly Dictionary<char, char> EnToRu = new();
    private static readonly Dictionary<char, char> RuToEn = new();

    static LayoutConverter()
    {
        foreach (var (en, ru) in PairsLower)
        {
            EnToRu[en] = ru;
            RuToEn[ru] = en;
        }
        foreach (var (en, ru) in PairsUpper)
        {
            EnToRu[en] = ru;
            RuToEn[ru] = en;
        }
    }

    /// <summary>EN -> RU.</summary>
    public static string ToRussian(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(EnToRu.TryGetValue(c, out var r) ? r : c);
        return sb.ToString();
    }

    /// <summary>RU -> EN.</summary>
    public static string ToEnglish(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(RuToEn.TryGetValue(c, out var e) ? e : c);
        return sb.ToString();
    }

    public enum Direction { None, ToRu, ToEn }

    /// <summary>Конвертация с возвратом выбранного направления.</summary>
    public static (string Result, Direction Dir) AutoConvertWithDirection(string text)
    {
        int latin = 0, cyr = 0;
        foreach (var c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            else if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё') cyr++;
        }
        if (latin == 0 && cyr == 0) return (text, Direction.None);
        return latin >= cyr ? (ToRussian(text), Direction.ToRu) : (ToEnglish(text), Direction.ToEn);
    }

    /// <summary>
    /// Эвристическое определение направления: если в строке больше латиницы — переводим в RU,
    /// иначе — в EN. Символы вне обеих раскладок не учитываются.
    /// </summary>
    public static string AutoConvert(string text) => AutoConvertWithDirection(text).Result;
}
