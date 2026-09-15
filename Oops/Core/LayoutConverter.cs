using System.Text;

namespace Oops.Core;

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

    /// <summary>
    /// Пропускать слова, которые и так выглядят настоящими словами своего
    /// языка. Включено по умолчанию; выключается настройкой на случай, когда
    /// модель ошиблась и хоткей молчит там, где не должен.
    /// </summary>
    public static bool SmartWordSelection { get; set; } = true;

    /// <summary>
    /// Насколько результат должен выглядеть правдоподобнее исходника, чтобы
    /// слово конвертировать. Запас смещён в сторону «не трогать»: испортить
    /// правильное слово хуже, чем оставить сломанное — сломанное человек
    /// дожмёт повторным нажатием, а испорченное посреди фразы придётся
    /// перенабирать руками.
    /// </summary>
    private const double PlausibilityMargin = 0.5;

    /// <summary>
    /// Конвертация с возвратом направления. Направление выбирается ДЛЯ КАЖДОГО
    /// СЛОВА ОТДЕЛЬНО.
    ///
    /// Иначе не лечится обычный случай: «Z djn [jxe pfgecnbnm ЬщвудКшыл», где
    /// первые слова набраны в английской раскладке вместо русской, а последнее —
    /// наоборот. При одном направлении на весь кусок побеждает большинство букв
    /// (16 латинских против 9), всё едет EN→RU, а кириллическое слово в этой
    /// таблице просто не встречается и проходит нетронутым. Ни одно нажатие
    /// такой текст не исправляло.
    ///
    /// Это НЕ возвращение `AutoConvertPerWord` из списка удалённого: то была
    /// автоматическая правка во время печати, где программа сама решала, что
    /// испорчено. Здесь границу по-прежнему задаёт человек — выделением или
    /// шагом области, — и пословно выбирается только сторона перевода внутри
    /// уже указанного куска.
    ///
    /// Цена известна: правильное слово на другом языке внутри выделения теперь
    /// тоже конвертируется. Но выделяют ради конвертации то, что считают
    /// сломанным, а при одном направлении такое слово ломалось бы ровно так же.
    /// </summary>
    public static (string Result, Direction Dir) AutoConvertWithDirection(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, Direction.None);

        var sb = new StringBuilder(text.Length);
        var lastDir = Direction.None;
        int skipped = 0;

        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            bool space = char.IsWhiteSpace(text[i]);
            while (i < text.Length && char.IsWhiteSpace(text[i]) == space) i++;
            var run = text.Substring(start, i - start);

            if (space) { sb.Append(run); continue; }

            var dir = DirectionOf(run);
            var converted = dir switch
            {
                Direction.ToRu => ToRussian(run),
                Direction.ToEn => ToEnglish(run),
                _ => run,
            };

            if (dir != Direction.None && SmartWordSelection && !WorthConverting(run, converted, dir))
            {
                // Слово и так выглядит настоящим — «appconfig», «nginx», «docker».
                // Конвертация превратила бы его в «фззсщташп».
                sb.Append(run);
                skipped++;
                continue;
            }

            sb.Append(converted);

            // Наружу отдаём направление ПОСЛЕДНЕГО слова, у которого оно есть:
            // каретка стоит в конце, и системную раскладку надо переключить под
            // то, что человек будет печатать дальше, а не под большинство уже
            // исправленного.
            if (dir != Direction.None) lastDir = dir;
        }

        // В лог идёт только ЧИСЛО пропущенных слов, не сами слова: набранный
        // текст в лог не попадает никогда.
        if (skipped > 0 && Log.Enabled)
            Log.Write($"пропущено слов как уже правдоподобные: {skipped}");

        return (sb.ToString(), lastDir);
    }

    /// <summary>
    /// Стоит ли конвертировать слово: похож ли результат на язык-цель сильнее,
    /// чем исходник похож на язык-источник.
    ///
    /// Это то самое место, где «xtuj» отличается от «appconfig». Оба полностью
    /// латинские, и подсчётом букв их не разделить — разница только в том, что
    /// одно является словом, а другое нет.
    /// </summary>
    private static bool WorthConverting(string original, string converted, Direction dir)
    {
        var from = dir == Direction.ToRu
            ? LanguageModel.Language.English
            : LanguageModel.Language.Russian;
        var to = dir == Direction.ToRu
            ? LanguageModel.Language.Russian
            : LanguageModel.Language.English;

        // Меньше — правдоподобнее, поэтому результат должен быть МЕНЬШЕ
        // исходника на величину запаса.
        return LanguageModel.Implausibility(converted, to) + PlausibilityMargin
             < LanguageModel.Implausibility(original, from);
    }

    /// <summary>
    /// Куда конвертировать этот кусок. Считаются только буквы: цифры и знаки
    /// есть в обеих раскладках и голоса не имеют — иначе «2024 (!!!)» решало бы
    /// судьбу слов вокруг себя.
    /// </summary>
    private static Direction DirectionOf(string word)
    {
        int latin = 0, cyr = 0;
        foreach (var c in word)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            else if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё') cyr++;
        }
        if (latin == 0 && cyr == 0) return Direction.None;
        return latin >= cyr ? Direction.ToRu : Direction.ToEn;
    }

    /// <summary>
    /// Эвристическое определение направления: если в строке больше латиницы — переводим в RU,
    /// иначе — в EN. Символы вне обеих раскладок не учитываются.
    /// </summary>
    public static string AutoConvert(string text) => AutoConvertWithDirection(text).Result;
}
