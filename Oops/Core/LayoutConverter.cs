using System.Linq;
using System.Text;

namespace Oops.Core;

/// <summary>
/// Конвертация текста между двумя раскладками — активной парой <see cref="Pair"/>.
///
/// Пара берётся из установленных в Windows и выбирается в настройках; по
/// умолчанию это английская и русская. Прошитая ниже таблица ЙЦУКЕН↔QWERTY —
/// встроенная пара на случай, когда системные раскладки прочитать нельзя (и
/// для тестов), а <see cref="ToRussian"/>/<see cref="ToEnglish"/> работают по ней.
/// </summary>
public static class LayoutConverter
{
    // Пары: символ EN <-> символ RU в одной и той же физической позиции клавиши.
    // Нижний регистр.
    public static readonly (char en, char ru)[] PairsLower =
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
    public static readonly (char en, char ru)[] PairsUpper =
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

    private static volatile LayoutPair? _pair;

    /// <summary>
    /// Между какими раскладками переводим. Ставит App из настроек и из списка
    /// раскладок Windows.
    ///
    /// По умолчанию — встроенная пара, и отдаётся она ЛЕНИВО, через геттер, а не
    /// инициализатором поля: встроенная пара сама строится из таблиц этого
    /// класса, и два статических инициализатора, ссылающихся друг на друга, при
    /// неудачном порядке оставили бы здесь null.
    /// </summary>
    public static LayoutPair Pair
    {
        get => _pair ?? LayoutPair.BuiltIn;
        set => _pair = value;
    }

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
    /// Иначе не лечится обычный случай: «Z djn [jxe gjckfnm уьфшд», где
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
    /// <param name="literal">
    /// Конвертировать КАЖДОЕ слово, не спрашивая модель языка. Так работает
    /// выделение: у него нет второго нажатия.
    ///
    /// Весь запас правдоподобия держится на том, что отказ поправим — слово
    /// дожимается повторным нажатием. Для выделения это неверно: следующее
    /// нажатие прочитает то же выделение и примет то же решение, и человек
    /// остаётся с «я думаю надо предусмотреть такую inere? потомму» без единого
    /// способа это исправить. Модель на биграммах иногда просто не видит
    /// разницы: «inere» как английское — 4.6, «штуку» как русское — 6.7.
    ///
    /// Границу здесь провёл человек, и провёл её руками. Цена известна и
    /// принята: настоящее иностранное слово внутри выделения тоже
    /// сконвертируется. Направление при этом по-прежнему выбирается ПОСЛОВНО,
    /// так что смешанный текст не ломается.
    /// </param>
    public static (string Result, KeyboardLayout? Target) Convert(string text, bool literal = false)
    {
        if (string.IsNullOrEmpty(text)) return (text, null);

        var pair = Pair;
        var runs = Split(text);
        bool smart = SmartWordSelection && !literal;

        var words = runs.Where(r => !r.IsSpace).ToList();
        bool single = words.Count == 1;
        var lettered = words.Where(w => w.Text.Any(char.IsLetter)).ToList();
        // Всё набрано заглавными — это включённый CapsLock, а не аббревиатуры:
        // «GHBDTN RFR LTKF» обязано стать «ПРИВЕТ КАК ДЕЛА».
        bool capsScope = lettered.Count > 0 && lettered.All(w => IsAllCaps(w.Text));

        // Первый проход: каждое слово судится само по себе, со полным запасом.
        Run? previousWord = null;
        foreach (var run in runs)
        {
            if (run.IsSpace) continue;
            var direction = pair.DirectionOf(run.Text);
            if (direction is not { } d) { previousWord = run; continue; }
            run.From = d.From;
            run.To = d.To;
            run.Converted = KeepNumbers(run.Text, d.From.ConvertTo(d.To, run.Text));
            run.Gain = PlausibilityGain(run.Text, run.Converted, d.From, d.To);

            // Аббревиатура: модель её судить не может. «УФНС» как русское и
            // «EAYC» как английское одинаково не похожи на слова, и на замерах
            // настоящие русские аббревиатуры получают выигрыш до +3.2 («ПДД»),
            // а английские, набранные в русской раскладке, — от −2.7 до +6.4.
            // Диапазоны перекрываются, порогом их не разделить. Поэтому решает
            // не модель, а то, на что указал человек: одно слово — переводим,
            // слово посреди фразы — не трогаем.
            bool acronym = IsAllCaps(run.Text);
            if (single && acronym)
                run.Convert = true;
            else if (smart && !single
                     && ((acronym && !capsScope) || IsUnitAfter(previousWord, run.Text) || IsGluedUnit(run.Text)))
            {
                run.Convert = false;
                run.Protected = true;
            }
            else
                run.Convert = !smart || run.Gain > PlausibilityMargin;

            previousWord = run;
        }

        // Второй проход: запас нужен не всегда.
        //
        // Запас защищает СОСЕДЕЙ — слова, на которые человек не показывал.
        // Испортить их хуже, чем недоделать работу: сломанное дожимается
        // повторным нажатием, а испорченное посреди фразы перенабирается
        // руками. Но там, где соседей нет или они заведомо сломаны, защищать
        // некого, и остаётся голый порог: результат просто должен выглядеть
        // правдоподобнее исходника.
        //
        // Беда в коротких словах: в трёх буквах слишком мало пар, чтобы
        // набрать полный запас. У «rfr» выигрыш 0.35 против 0.5 — и «привет
        // rfr дела» оставалось ровно тем месивом, ради которого всё
        // затевалось, а одиночное нажатие на последнем слове «rfr» вообще
        // молчало.
        //
        // Снять запас безопасно: на замерах ВСЁ настоящее уходит в минус —
        // «email» −6.1, «password» −6.0, «online» −4.0, «Anderson» −4.0,
        // «tot» −1.3, «photo.jpg» −0.7, «https://example.com» −0.4,
        // «get» −0.2. Порог их
        // держит и без запаса.
        if (smart)
            foreach (var run in runs)
                if (!run.IsSpace && run.To != null && !run.Convert && !run.Protected && run.Gain > 0
                    && (single || HasConvertedNeighbour(runs, run)))
                    run.Convert = true;

        var sb = new StringBuilder(text.Length);
        KeyboardLayout? target = null;
        int skipped = 0;

        foreach (var run in runs)
        {
            if (run.IsSpace) { sb.Append(run.Text); continue; }

            if (run.To != null && !run.Convert)
            {
                // Слово и так выглядит настоящим — «password», «email», «Anderson».
                // Конвертация превратила бы его в «зфыыцщкв».
                sb.Append(run.Text);
                skipped++;
                continue;
            }

            sb.Append(run.To != null ? run.Converted : run.Text);

            // Наружу отдаём раскладку ПОСЛЕДНЕГО сконвертированного слова:
            // каретка стоит в конце, и системную раскладку надо переключить под
            // то, что человек будет печатать дальше, а не под большинство уже
            // исправленного.
            if (run.To != null) target = run.To;
        }

        // В лог идёт только ЧИСЛО пропущенных слов, не сами слова: набранный
        // текст в лог не попадает никогда.
        if (skipped > 0 && Log.Enabled)
            Log.Write($"пропущено слов как уже правдоподобные: {skipped}");

        return (sb.ToString(), target);
    }

    /// <summary>
    /// Прежний вид результата — направлением RU/EN. Оставлен для тестов, которые
    /// работают со встроенной парой; программа пользуется <see cref="Convert"/>.
    /// </summary>
    public static (string Result, Direction Dir) AutoConvertWithDirection(
        string text, bool literal = false)
    {
        var (result, target) = Convert(text, literal);
        var dir = target == null ? Direction.None
            : target.Language == "en" ? Direction.ToEn
            : Direction.ToRu;
        return (result, dir);
    }

    private sealed class Run
    {
        public string Text = string.Empty;
        public bool IsSpace;
        /// <summary>Откуда и куда; null — в слове нет букв ни одной раскладки пары.</summary>
        public KeyboardLayout? From;
        public KeyboardLayout? To;
        public string Converted = string.Empty;
        public double Gain;
        public bool Convert;
        /// <summary>
        /// Аббревиатура или единица измерения посреди фразы: не трогаем, и в
        /// соседи слову не годится — она ничего не говорит о том, сломан ли текст вокруг.
        /// </summary>
        public bool Protected;
    }

    /// <summary>Две буквы и больше, и все заглавные — «УФНС», «API», «ЬФСИЩЩЛ».</summary>
    private static bool IsAllCaps(string word)
    {
        int letters = 0;
        foreach (var c in word)
        {
            if (!char.IsLetter(c)) continue;
            if (!char.IsUpper(c)) return false;
            letters++;
        }
        return letters >= 2;
    }

    /// <summary>
    /// Единица после числа: «5 кг», «100 гб». Короткое слово сразу за числом —
    /// почти всегда единица, набранная как надо, а конвертация делала из «кг»
    /// латинское «ru».
    /// </summary>
    private static bool IsUnitAfter(Run? previous, string word)
    {
        if (previous == null || word.Count(char.IsLetter) > 3) return false;
        var number = previous.Text;
        return number.Any(char.IsDigit) && number.All(c => char.IsDigit(c) || c == '.' || c == ',');
    }

    /// <summary>Число и единица слитно: «5,5кг», «100гб».</summary>
    private static bool IsGluedUnit(string word)
    {
        int i = 0;
        while (i < word.Length && (char.IsDigit(word[i]) || word[i] == '.' || word[i] == ','))
            i++;
        if (i == 0 || !word.Take(i).Any(char.IsDigit)) return false;
        int letters = word.Length - i;
        return letters is >= 1 and <= 3 && word.Skip(i).All(char.IsLetter);
    }

    /// <summary>
    /// Цифры и разделитель между цифрами не конвертируются никогда. Позиционно
    /// «5,5» из русской раскладки — это «5?5»: запятая на русской клавиатуре
    /// стоит там, где у английской вопросительный знак. Число человек набрал
    /// правильно в любой раскладке; длина при этом не меняется.
    /// </summary>
    private static string KeepNumbers(string original, string converted)
    {
        var chars = converted.ToCharArray();
        for (int i = 0; i < original.Length; i++)
        {
            char c = original[i];
            bool betweenDigits = i > 0 && i < original.Length - 1
                && char.IsDigit(original[i - 1]) && char.IsDigit(original[i + 1]) && !char.IsLetter(c);
            if (char.IsDigit(c) || betweenDigits) chars[i] = c;
        }
        return new string(chars);
    }

    private static List<Run> Split(string text)
    {
        var runs = new List<Run>();
        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            bool space = char.IsWhiteSpace(text[i]);
            while (i < text.Length && char.IsWhiteSpace(text[i]) == space) i++;
            runs.Add(new Run { Text = text.Substring(start, i - start), IsSpace = space });
        }
        return runs;
    }

    /// <summary>
    /// Есть ли рядом слово, которое решилось конвертироваться в ту же сторону
    /// само, без всякой поддержки. Соседом считается ближайшее слово слева и
    /// справа: пробелы между ними роли не играют.
    /// </summary>
    private static bool HasConvertedNeighbour(List<Run> runs, Run run)
    {
        int at = runs.IndexOf(run);
        return Neighbour(runs, at, -1) || Neighbour(runs, at, +1);

        static bool Neighbour(List<Run> runs, int at, int step)
        {
            for (int i = at + step; i >= 0 && i < runs.Count; i += step)
            {
                // Числа и защищённые слова о тексте вокруг ничего не говорят и
                // в соседи не годятся: иначе «rfr 5 кг ltkf» оставлял «rfr» —
                // его соседом оказывалось «5», а оно не конвертируется никогда.
                if (runs[i].IsSpace || runs[i].To == null || runs[i].Protected) continue;
                return runs[i].Convert && runs[i].To == runs[at].To;
            }
            return false;
        }
    }

    /// <summary>
    /// Насколько результат правдоподобнее исходника: больше нуля — похоже, что
    /// слово набрано не в той раскладке.
    ///
    /// Это то самое место, где «xtuj» отличается от «password». Оба полностью
    /// латинские, и подсчётом букв их не разделить — разница только в том, что
    /// одно является словом, а другое нет.
    ///
    /// Модели языка есть не для всех языков. Если её нет хотя бы для одной
    /// стороны, судить нечем, и выигрыш считается бесконечным: слово
    /// конвертируется, как конвертировалось всё до появления модели. Молчать на
    /// паре без модели было бы хуже — хоткей просто перестал бы работать.
    /// </summary>
    private static double PlausibilityGain(string original, string converted,
        KeyboardLayout from, KeyboardLayout to)
    {
        if (!LanguageModel.Supports(from.Language) || !LanguageModel.Supports(to.Language))
            return double.PositiveInfinity;

        // Меньше — правдоподобнее, поэтому выигрыш положителен, когда результат
        // выглядит лучше исходника.
        return LanguageModel.Implausibility(original, from.Language)
             - LanguageModel.Implausibility(converted, to.Language);
    }

    /// <summary>Конвертация без подробностей — только текст.</summary>
    public static string AutoConvert(string text) => Convert(text).Result;
}
