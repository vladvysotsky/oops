namespace Oops.Core;

/// <summary>
/// Модель «расширяющейся области».
///
/// Программа НЕ угадывает, где началась неправильная раскладка. Границу задаёт
/// пользователь повторными нажатиями хоткея:
///   1-е нажатие — последнее слово,
///   2-е        — весь набранный текст.
///
/// Два шага, а не рост по одному слову: на фразе из пяти слов пословное
/// расширение потребовало бы пяти нажатий, и до конца никто не дожимал.
/// Практически всегда портится либо последнее слово, либо всё сразу.
///
/// Каждый шаг — тупое преобразование 1-в-1 чётко очерченного куска. Никаких
/// словарей, никаких догадок. Корректный текст вне области не трогается.
///
/// Ключевое свойство, на котором всё держится: и смена раскладки, и смена
/// регистра СОХРАНЯЮТ количество символов. Поэтому «сколько сейчас на экране
/// от начала области до каретки» всегда равно (Original.Length - scopeStart),
/// независимо от того, сколько раз мы уже переписывали текст.
/// </summary>
public sealed class ScopeEditor
{
    /// <summary>Как долго последующее нажатие продолжает расширять ту же область.</summary>
    public TimeSpan ExpandWindow { get; set; } = TimeSpan.FromSeconds(2);

    // Хук вызывает ResetSession из своего потока, шаги выполняются в UI-потоке.
    private readonly object _gate = new();
    private string _original = string.Empty;
    private DateTime _lastPressUtc = DateTime.MinValue;
    private int _step;              // 0 — сессии нет, 1 — последнее слово, 2 — всё
    private Kind _kind = Kind.None;

    private enum Kind { None, Layout, Case }

    /// <summary>Что нужно сделать с текстом на экране.</summary>
    /// <param name="EraseCount">Сколько символов стереть Backspace'ами.</param>
    /// <param name="Text">Что напечатать вместо них.</param>
    /// <param name="Direction">Куда переключить системную раскладку (только для Layout).</param>
    /// <param name="NewBufferContent">Каким должен стать буфер после операции.</param>
    public readonly record struct Edit(int EraseCount, string Text, LayoutConverter.Direction Direction, string NewBufferContent)
    {
        public bool IsEmpty => EraseCount == 0 && Text.Length == 0;
        public static readonly Edit None = new(0, string.Empty, LayoutConverter.Direction.None, string.Empty);
    }

    /// <summary>Нажали хоткей смены раскладки.</summary>
    public Edit NextLayoutStep(string currentBuffer, DateTime nowUtc)
        => NextStep(Kind.Layout, currentBuffer, nowUtc);

    /// <summary>Нажали хоткей смены регистра.</summary>
    public Edit NextCaseStep(string currentBuffer, DateTime nowUtc)
        => NextStep(Kind.Case, currentBuffer, nowUtc);

    /// <summary>Прервать расширение — пользователь напечатал что-то новое, кликнул и т.п.</summary>
    public void ResetSession()
    {
        lock (_gate)
        {
            _kind = Kind.None;
            _step = 0;
            _original = string.Empty;
        }
    }

    private Edit NextStep(Kind kind, string currentBuffer, DateTime nowUtc)
    {
        lock (_gate) return NextStepLocked(kind, currentBuffer, nowUtc);
    }

    private Edit NextStepLocked(Kind kind, string currentBuffer, DateTime nowUtc)
    {
        bool continuing =
            _kind == kind &&
            _step > 0 &&
            nowUtc - _lastPressUtc <= ExpandWindow;

        if (!continuing)
        {
            // Новая сессия: замораживаем то, что сейчас в буфере, как эталон.
            _original = currentBuffer;
            _step = 0;
            _kind = kind;
        }

        if (string.IsNullOrEmpty(_original)) { ResetSession(); return Edit.None; }
        if (TypingBuffer.CountWords(_original) == 0) { ResetSession(); return Edit.None; }

        int nextStep = _step + 1;

        // Шаг 1 — последнее слово, шаг 2 — весь набранный текст.
        int scopeStart = nextStep == 1
            ? TypingBuffer.StartOfLastWords(_original, 1)
            : 0;

        // Расширять больше некуда: шагов всего два, а на однословном буфере второй
        // шаг захватил бы ровно тот же кусок.
        bool exhausted = nextStep > 2
            || (nextStep == 2 && scopeStart == TypingBuffer.StartOfLastWords(_original, 1));
        if (exhausted)
        {
            // Время последнего шага НЕ обновляем. Иначе частые повторные нажатия
            // бесконечно продлевают окно расширения, и вместо начала новой сессии
            // пользователь получает полную тишину — «хоткей не работает».
            _step = 2;
            return Edit.None;
        }

        _lastPressUtc = nowUtc;
        _step = nextStep;

        var scope = _original.Substring(scopeStart);

        string converted;
        var dir = LayoutConverter.Direction.None;
        if (kind == Kind.Layout)
        {
            (converted, dir) = LayoutConverter.AutoConvertWithDirection(scope);
        }
        else
        {
            converted = ToggleCase(scope);
        }

        // Вся арифметика стирания держится на сохранении длины. Если преобразование
        // почему-то изменило её (экзотический Unicode), мы больше не знаем, сколько
        // символов на экране — безопаснее не трогать текст вообще.
        if (converted.Length != scope.Length) { ResetSession(); return Edit.None; }

        // Длина сохраняется — значит на экране от scopeStart до каретки ровно столько же
        // символов, сколько в _original, сколько бы раз мы уже ни переписывали.
        int eraseCount = _original.Length - scopeStart;
        var newBuffer = _original.Substring(0, scopeStart) + converted;

        return new Edit(eraseCount, converted, dir, newBuffer);
    }

    /// <summary>
    /// Есть заглавная → всё в нижний, иначе всё в верхний.
    /// Инвариантная культура намеренно: культурные правила (турецкая «i» и т.п.)
    /// дали бы результат, зависящий от локали системы.
    /// </summary>
    public static string ToggleCase(string text)
    {
        foreach (var c in text)
            if (char.IsUpper(c)) return text.ToLowerInvariant();
        return text.ToUpperInvariant();
    }
}
