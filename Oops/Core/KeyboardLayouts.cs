using System.Text;

namespace Oops.Core;

/// <summary>Физическая клавиша: виртуальный код и зажат ли Shift.</summary>
public readonly record struct KeyStroke(ushort Vk, bool Shift);

/// <summary>
/// Раскладка как карта «клавиша ↔ символ».
///
/// Конвертация между раскладками — ПОЗИЦИОННАЯ: символ ищется на клавише в
/// исходной раскладке, и берётся то, что та же клавиша даёт в целевой. Это
/// ровно то, что раньше было прошито таблицей ЙЦУКЕН↔QWERTY, только теперь
/// карту для любой установленной раскладки строит сама Windows
/// (<see cref="SystemLayouts"/>), а прошитая таблица осталась встроенной
/// парой на случай, когда системные раскладки прочитать не удалось.
///
/// Длина при конвертации сохраняется по построению: один символ всегда
/// становится одним символом. На этом держится вся арифметика стирания в
/// <see cref="ScopeEditor"/>.
/// </summary>
public sealed class KeyboardLayout
{
    private readonly Dictionary<char, KeyStroke> _keyOf = new();
    private readonly Dictionary<KeyStroke, char> _charAt = new();

    /// <param name="id">Как раскладка записывается в настройки. Для системных — HKL.</param>
    /// <param name="language">Язык, ISO 639-1 («ru», «en», «uk»). По нему выбирается модель языка.</param>
    /// <param name="displayName">Как раскладку показать человеку.</param>
    /// <param name="handle">HKL для переключения системной раскладки; ноль у встроенных.</param>
    public KeyboardLayout(string id, string language, string displayName, IntPtr handle,
        IEnumerable<(KeyStroke Key, char Char)> keys)
    {
        Id = id;
        Language = language;
        DisplayName = displayName;
        Handle = handle;
        foreach (var (key, ch) in keys)
        {
            _charAt.TryAdd(key, ch);
            // Символ, который дают две клавиши, привязываем к первой встреченной:
            // перебор идёт от букв к знакам, и «настоящее» место буквы раньше.
            _keyOf.TryAdd(ch, key);
        }
    }

    public string Id { get; }
    public string Language { get; }
    public string DisplayName { get; }
    public IntPtr Handle { get; }

    /// <summary>Сколько букв слова эта раскладка вообще умеет набрать.</summary>
    public int LettersOf(string word)
    {
        int n = 0;
        foreach (var c in word)
            if (char.IsLetter(c) && _keyOf.ContainsKey(c)) n++;
        return n;
    }

    /// <summary>
    /// Текст, набранный в этой раскладке, — каким он вышел бы в <paramref name="target"/>.
    /// Символ, которого здесь нет или которому там нет пары, остаётся как был.
    /// </summary>
    public string ConvertTo(KeyboardLayout target, string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(_keyOf.TryGetValue(c, out var key) && target._charAt.TryGetValue(key, out var t) ? t : c);
        return sb.ToString();
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Две раскладки, между которыми программа переводит текст.
///
/// Пара, а не «все установленные»: с тремя раскладками (RU, UA, EN) у слова,
/// набранного латиницей, два кандидата, и угадывать между ними — ровно то
/// угадывание, от которого программа отказалась. Пару человек выбирает в
/// настройках сам, а по умолчанию это английская и русская.
/// </summary>
public sealed class LayoutPair
{
    public LayoutPair(KeyboardLayout first, KeyboardLayout second)
    {
        First = first;
        Second = second;
    }

    public KeyboardLayout First { get; }
    public KeyboardLayout Second { get; }

    /// <summary>
    /// В какой раскладке набрано слово и куда его переводить, или null, если
    /// в слове нет букв ни одной из двух. Считаются только буквы: цифры и знаки
    /// есть в обеих раскладках и голоса не имеют — иначе «2024 (!!!)» решало бы
    /// судьбу слов вокруг себя. При равенстве побеждает первая раскладка.
    /// </summary>
    public (KeyboardLayout From, KeyboardLayout To)? DirectionOf(string word)
    {
        int first = First.LettersOf(word), second = Second.LettersOf(word);
        if (first == 0 && second == 0) return null;
        return first >= second ? (First, Second) : (Second, First);
    }

    /// <summary>
    /// Какую пару взять из установленных раскладок.
    ///
    /// Выбранная в настройках — если обе раскладки всё ещё установлены и это
    /// две разные раскладки. Иначе пара по умолчанию: английская и русская,
    /// если они есть, а если нет — первые две по порядку. Раскладку могли
    /// удалить из Windows после того, как её выбрали, и молча замолчать из-за
    /// этого программа не должна.
    ///
    /// null — установлено меньше двух раскладок, переводить не между чем.
    /// </summary>
    public static LayoutPair? Choose(IReadOnlyList<KeyboardLayout> installed, string? firstId, string? secondId)
    {
        if (installed.Count < 2) return null;

        var first = installed.FirstOrDefault(l => l.Id == firstId);
        var second = installed.FirstOrDefault(l => l.Id == secondId);
        if (first != null && second != null && first != second) return new LayoutPair(first, second);

        var defaultFirst = installed.FirstOrDefault(l => l.Language == "en") ?? installed[0];
        var defaultSecond = installed.FirstOrDefault(l => l.Language == "ru" && l != defaultFirst)
                            ?? installed.First(l => l != defaultFirst);
        return new LayoutPair(defaultFirst, defaultSecond);
    }

    /// <summary>
    /// Встроенная пара EN↔RU из прошитой таблицы. Работает, когда системные
    /// раскладки прочитать не вышло, и в тестах, где Windows нет.
    /// </summary>
    public static LayoutPair BuiltIn { get; } = BuildBuiltIn();

    private static LayoutPair BuildBuiltIn()
    {
        // Ключ клавиши здесь условный — номер пары в таблице. Настоящие коды
        // клавиш встроенной паре не нужны: важно лишь, что обе раскладки
        // ссылаются на одну и ту же «клавишу».
        var en = new List<(KeyStroke, char)>();
        var ru = new List<(KeyStroke, char)>();
        void Add((char en, char ru)[] pairs, bool shift)
        {
            for (int i = 0; i < pairs.Length; i++)
            {
                var key = new KeyStroke((ushort)i, shift);
                en.Add((key, pairs[i].en));
                ru.Add((key, pairs[i].ru));
            }
        }
        Add(LayoutConverter.PairsLower, shift: false);
        Add(LayoutConverter.PairsUpper, shift: true);
        return new LayoutPair(
            new KeyboardLayout("builtin-en", "en", "English", IntPtr.Zero, en),
            new KeyboardLayout("builtin-ru", "ru", "Русский", IntPtr.Zero, ru));
    }
}
