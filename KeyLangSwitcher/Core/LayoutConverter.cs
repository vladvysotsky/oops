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
        // Shift+цифры: один и тот же физический Shift+N на разных раскладках
        // даёт разные символы. Сопоставляем их позиционно.
        ('@','"'),  // Shift+2
        ('#','№'),  // Shift+3
        ('$',';'),  // Shift+4
        ('^',':'),  // Shift+6
        ('&','?'),  // Shift+7
        ('|','/'),  // Shift+\ : US "|" ↔ RU "/"
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

    /// <summary>
    /// Пословная конвертация: проходит по тексту, для каждого слова запускает AutoDetector,
    /// и конвертирует ТОЛЬКО те слова, которые набраны в неправильной раскладке.
    /// Также возвращает признак того, что хотя бы одно слово было ОПОЗНАНО как правильное
    /// в своём языке — в этом случае вызывающий код может НЕ делать whole-buffer fallback,
    /// иначе он перевернёт корректный текст в гибериш.
    /// </summary>
    public static (string Result, Direction FinalDirection, bool AnyChange, bool AnyKnown) AutoConvertPerWord(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, Direction.None, false, false);

        var sb = new System.Text.StringBuilder(text.Length);
        Direction lastConvertDir = Direction.None;
        bool anyChange = false;
        bool anyKnown = false;
        int i = 0;
        while (i < text.Length)
        {
            int sepStart = i;
            while (i < text.Length && !IsLetter(text[i])) i++;
            if (i > sepStart) sb.Append(text, sepStart, i - sepStart);
            if (i >= text.Length) break;

            int wordStart = i;
            bool firstIsLatin = IsLatin(text[i]);
            while (i < text.Length && IsLetter(text[i]) && IsLatin(text[i]) == firstIsLatin) i++;
            var word = text.Substring(wordStart, i - wordStart);

            var verdict = AutoDetector.Analyze(word);
            if (verdict == AutoDetector.Verdict.WasMeantRussian)
            {
                sb.Append(ToRussian(word));
                lastConvertDir = Direction.ToRu;
                anyChange = true;
            }
            else if (verdict == AutoDetector.Verdict.WasMeantEnglish)
            {
                sb.Append(ToEnglish(word));
                lastConvertDir = Direction.ToEn;
                anyChange = true;
            }
            else
            {
                sb.Append(word);
                // Keep — но только если слово действительно опознано в своём языке (есть в словаре).
                // Это отличает "правильное слово" от "неизвестного шума".
                if (IsKnownInOwnLanguage(word)) anyKnown = true;
            }
        }
        return (sb.ToString(), lastConvertDir, anyChange, anyKnown);
    }

    private static bool IsKnownInOwnLanguage(string word)
    {
        var lower = word.ToLowerInvariant();
        bool isLatin = lower.Length > 0 && IsLatin(lower[0]);
        if (isLatin)
            return WordDictionary.IsKnownEn(lower) || WordDictionary.IsKnownEnFuzzy(lower);
        return WordDictionary.IsKnownRu(lower) || WordDictionary.IsKnownRuFuzzy(lower);
    }

    private static bool IsLetter(char c) => IsLatin(c) || IsCyrillic(c);
    private static bool IsLatin(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    private static bool IsCyrillic(char c) =>
        (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё';
}
