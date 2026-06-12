using System.IO;
using System.Reflection;
using System.Text;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Лексический словарь для авто-определения раскладки.
/// Базовый словарь — встроен в сборку (Resources/words_en.txt и words_ru.txt).
/// Пользователь может расширить его файлами %AppData%/KeyLangSwitcher/words_*.user.txt —
/// одно слово на строку, в нижнем регистре, строки с '#' игнорируются.
/// </summary>
public static class WordDictionary
{
    private static readonly HashSet<string> _en = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _ru = new(StringComparer.Ordinal);

    static WordDictionary()
    {
        LoadEmbedded("KeyLangSwitcher.Resources.words_en.txt", _en, normalize: null);
        LoadEmbedded("KeyLangSwitcher.Resources.words_ru.txt", _ru, normalize: NormalizeRu);
        LoadUserDictionary("words_en.user.txt", _en, normalize: null);
        LoadUserDictionary("words_ru.user.txt", _ru, normalize: NormalizeRu);
    }

    public static int EnCount => _en.Count;
    public static int RuCount => _ru.Count;

    public static bool IsKnownEn(string lowerWord) => _en.Contains(lowerWord);

    /// <summary>
    /// Все RU-слова нормализуются ё→е и на ВСТАВКЕ и на поиске — 'ещё' и 'еще'
    /// считаются одним словом (типичная русская привычка не ставить точки над ё).
    /// </summary>
    public static bool IsKnownRu(string lowerWord) => _ru.Contains(NormalizeRu(lowerWord));

    /// <summary>
    /// Fuzzy-проверка: считаем слово знакомым, если в словаре есть слово с edit-distance ≤ 1
    /// (одна замена / пропуск / лишняя буква / перестановка соседних). Включается для
    /// поддержки типичных опечаток вроде 'привкт' (привет), 'прривет' (двойная буква),
    /// 'пирвет' (перестановка).
    /// </summary>
    public static bool IsKnownEnFuzzy(string lowerWord)
        => Contains(_en, lowerWord, EnAlphabet);

    public static bool IsKnownRuFuzzy(string lowerWord)
        => Contains(_ru, NormalizeRu(lowerWord), RuAlphabet);

    private const string EnAlphabet = "abcdefghijklmnopqrstuvwxyz";
    // Без ё — мы нормализуем к 'е'.
    private const string RuAlphabet = "абвгдежзийклмнопрстуфхцчшщъыьэюя";

    private static bool Contains(HashSet<string> dict, string word, string alphabet)
    {
        if (dict.Contains(word)) return true;
        if (word.Length < 3) return false; // на коротких словах fuzzy даёт слишком много ложных срабатываний

        // Замена одной буквы.
        var chars = word.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char orig = chars[i];
            foreach (var c in alphabet)
            {
                if (c == orig) continue;
                chars[i] = c;
                if (dict.Contains(new string(chars))) return true;
            }
            chars[i] = orig;
        }

        // Удаление одной буквы.
        for (int i = 0; i < word.Length; i++)
        {
            var candidate = word.Remove(i, 1);
            if (dict.Contains(candidate)) return true;
        }

        // Вставка одной буквы.
        for (int i = 0; i <= word.Length; i++)
        {
            foreach (var c in alphabet)
            {
                var candidate = word.Insert(i, c.ToString());
                if (dict.Contains(candidate)) return true;
            }
        }

        // Перестановка соседних букв.
        for (int i = 0; i + 1 < word.Length; i++)
        {
            var arr = word.ToCharArray();
            (arr[i], arr[i + 1]) = (arr[i + 1], arr[i]);
            if (dict.Contains(new string(arr))) return true;
        }

        return false;
    }

    private static string NormalizeRu(string s) =>
        s.IndexOf('ё') >= 0 ? s.Replace('ё', 'е') : s;

    private static void LoadEmbedded(string resourceName, HashSet<string> target, Func<string, string>? normalize)
    {
        var asm = typeof(WordDictionary).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return;
        Read(stream, target, normalize);
    }

    private static void LoadUserDictionary(string fileName, HashSet<string> target, Func<string, string>? normalize)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KeyLangSwitcher");
            var path = Path.Combine(dir, fileName);
            if (!File.Exists(path)) return;
            using var s = File.OpenRead(path);
            Read(s, target, normalize);
        }
        catch { /* пользовательский словарь — необязательный */ }
    }

    private static void Read(Stream s, HashSet<string> target, Func<string, string>? normalize)
    {
        using var r = new StreamReader(s, Encoding.UTF8);
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var word = line.ToLowerInvariant();
            if (normalize != null) word = normalize(word);
            target.Add(word);
        }
    }
}
