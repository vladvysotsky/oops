using System.IO;
using System.Text;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Хранит список слов, которые автокоррекция не должна трогать.
/// Пользователь "обучает" приложение, откатывая ошибочную автокоррекцию —
/// тогда исходное слово добавляется сюда и больше не трогается.
/// Файл: %AppData%\KeyLangSwitcher\never_fix.txt (одно слово на строку, lowercase).
/// </summary>
public sealed class NeverFixList
{
    private readonly HashSet<string> _words = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly object _gate = new();

    public NeverFixList()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyLangSwitcher");
        _path = Path.Combine(dir, "never_fix.txt");
        Load();
    }

    public bool Contains(string word)
    {
        lock (_gate) return _words.Contains(word.ToLowerInvariant());
    }

    public void Add(string word)
    {
        var lower = word.ToLowerInvariant();
        lock (_gate)
        {
            if (!_words.Add(lower)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, lower + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* список — best-effort, не критично если не сохранился */ }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            foreach (var raw in File.ReadAllLines(_path, Encoding.UTF8))
            {
                var s = raw.Trim();
                if (s.Length == 0 || s[0] == '#') continue;
                _words.Add(s.ToLowerInvariant());
            }
        }
        catch { }
    }
}
