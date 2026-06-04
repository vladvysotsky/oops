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
        LoadEmbedded("KeyLangSwitcher.Resources.words_en.txt", _en);
        LoadEmbedded("KeyLangSwitcher.Resources.words_ru.txt", _ru);
        LoadUserDictionary("words_en.user.txt", _en);
        LoadUserDictionary("words_ru.user.txt", _ru);
    }

    public static int EnCount => _en.Count;
    public static int RuCount => _ru.Count;

    public static bool IsKnownEn(string lowerWord) => _en.Contains(lowerWord);
    public static bool IsKnownRu(string lowerWord) => _ru.Contains(lowerWord);

    private static void LoadEmbedded(string resourceName, HashSet<string> target)
    {
        var asm = typeof(WordDictionary).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return;
        Read(stream, target);
    }

    private static void LoadUserDictionary(string fileName, HashSet<string> target)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KeyLangSwitcher");
            var path = Path.Combine(dir, fileName);
            if (!File.Exists(path)) return;
            using var s = File.OpenRead(path);
            Read(s, target);
        }
        catch { /* пользовательский словарь — необязательный */ }
    }

    private static void Read(Stream s, HashSet<string> target)
    {
        using var r = new StreamReader(s, Encoding.UTF8);
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            target.Add(line.ToLowerInvariant());
        }
    }
}
