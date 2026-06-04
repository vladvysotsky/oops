using System.Windows.Forms;
using KeyLangSwitcher.Core;
using KeyLangSwitcher.Hooks;
using KeyLangSwitcher.Settings;

namespace KeyLangSwitcher;

/// <summary>
/// Главный координатор: связывает хуки, буфер, конвертер и хоткей.
/// </summary>
public sealed class App : IDisposable
{
    public AppSettings Settings { get; }
    private readonly TypingBuffer _buffer = new();
    private readonly LayoutTracker _layoutTracker = new();
    private readonly NeverFixList _neverFix = new();
    private readonly KeyboardHook _kbHook = new();
    private readonly MouseHook _mouseHook = new();
    private readonly ForegroundWatcher _fgWatcher = new();

    // Запись о последней автокоррекции — для отката по Pause/Break
    // и обучения по немедленному Backspace.
    private sealed record AutoCorrection(string Original, string Corrected, char Separator, DateTime At, LayoutConverter.Direction Dir);
    private AutoCorrection? _lastAutoCorrection;
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(5);

    // UI-поток нужен для clipboard-операций
    private readonly SynchronizationContext _uiContext;

    public App(AppSettings settings)
    {
        Settings = settings;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("App must be created on the UI thread");

        ApplySettings();

        _kbHook.KeyDown += OnKeyDown;
        _mouseHook.Clicked += (_, _) => _buffer.Clear();
        _fgWatcher.ForegroundChanged += (_, _) =>
        {
            _buffer.Clear();
            _layoutTracker.Reset();
        };

        _kbHook.Install();
        _mouseHook.Install();
        _fgWatcher.Install();
    }

    public void ApplySettings()
    {
        _buffer.IdleTimeout = TimeSpan.FromSeconds(Settings.BufferIdleTimeoutSeconds);
    }

    private void OnKeyDown(object? sender, KeyboardHook.KeyEvent e)
    {
        if (!Settings.Enabled) return;

        // 0) Если пользователь сам сменил раскладку (Alt+Shift, Win+Space и т.п.) —
        //    дальнейшие нажатия будут идти в другой системе букв, наш буфер уже
        //    не соответствует тому, что попадает на экран. Сбрасываем.
        if (_layoutTracker.LayoutChangedSinceLastCheck())
            _buffer.Clear();

        // 1) Хоткей конвертации
        if (Settings.ConvertHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            // Глотаем событие, чтобы оно не дошло до приложения
            e.Handled = true;

            // Выполняем в UI-потоке — нужен для clipboard
            _uiContext.Post(_ => RunConvert(), null);
            return;
        }

        // 1.5) Pause/Break — отмена последней автокоррекции.
        if (e.VirtualKey == Keys.Pause && _lastAutoCorrection != null)
        {
            e.Handled = true;
            _uiContext.Post(_ => UndoLastAutoCorrection(), null);
            return;
        }

        // 1.6) Backspace сразу после автокоррекции — пользователь явно её не хотел.
        //      Откатываем и запоминаем оригинальное слово как "не трогать".
        if (e.VirtualKey == Keys.Back
            && _lastAutoCorrection != null
            && DateTime.UtcNow - _lastAutoCorrection.At < UndoWindow)
        {
            var lc = _lastAutoCorrection;
            _lastAutoCorrection = null;
            e.Handled = true;
            _uiContext.Post(_ =>
            {
                _neverFix.Add(lc.Original);
                UndoCorrection(lc);
            }, null);
            return;
        }

        // 2) Навигация / редактирование, влияющее на буфер
        switch (e.VirtualKey)
        {
            case Keys.Back:
                _buffer.Backspace();
                return;
            case Keys.Delete:
                _buffer.Delete();
                return;
            case Keys.Left:
                _buffer.MoveLeft();
                return;
            case Keys.Right:
                _buffer.MoveRight();
                return;
            case Keys.Home:
                _buffer.MoveHome();
                return;
            case Keys.End:
                _buffer.MoveEnd();
                return;
            // Вертикальная навигация и завершающие действия — мы теряем контекст.
            case Keys.Up:
            case Keys.Down:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Enter:
            case Keys.Tab:
            case Keys.Escape:
                _buffer.Clear();
                return;
        }

        // 3) Накопление символов
        if (e.TypedChar.HasValue)
        {
            var c = e.TypedChar.Value;
            _buffer.Append(c);

            // 4) Авто-правка на разделителе: layout + typography.
            if ((Settings.AutoDetectWrongLayout || Settings.AutoFixTypography) && IsWordSeparator(c))
                _uiContext.Post(_ => TryAutoFixLastWord(), null);
        }
    }

    private static bool IsWordSeparator(char c) =>
        c == ' ' || c == '\t' || c == ',' || c == '.' || c == ';' || c == ':'
        || c == '!' || c == '?' || c == '/' || c == '\\' || c == '"' || c == '\''
        || c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}';

    private void TryAutoFixLastWord()
    {
        var snap = _buffer.Snapshot();
        if (snap.Length < 2) return;
        var sep = snap[^1];

        int wordEnd = snap.Length - 1;
        int wordStart = wordEnd;
        while (wordStart > 0 && !IsWordSeparator(snap[wordStart - 1])) wordStart--;
        var word = snap.Substring(wordStart, wordEnd - wordStart);
        if (word.Length == 0) return;

        // Пользователь уже один раз отказался от автокоррекции этого слова —
        // больше не трогаем.
        if (_neverFix.Contains(word)) return;

        string corrected = word;
        var layoutDir = LayoutConverter.Direction.None;

        // 1) Слой раскладки.
        if (Settings.AutoDetectWrongLayout)
        {
            var verdict = AutoDetector.Analyze(corrected);
            if (verdict == AutoDetector.Verdict.WasMeantRussian)
            {
                corrected = LayoutConverter.ToRussian(corrected);
                layoutDir = LayoutConverter.Direction.ToRu;
            }
            else if (verdict == AutoDetector.Verdict.WasMeantEnglish)
            {
                corrected = LayoutConverter.ToEnglish(corrected);
                layoutDir = LayoutConverter.Direction.ToEn;
            }
        }

        // 2) Типографика.
        if (Settings.AutoFixTypography)
        {
            corrected = Typography.FixAccidentalCapsLock(corrected) ?? corrected;
            corrected = Typography.FixDoubleCapital(corrected) ?? corrected;
        }

        if (corrected == word) return;

        Sender.ReleaseHotkeyModifiers();
        Sender.SendBackspaces(word.Length + 1);
        ClipboardPaste.Paste(corrected + sep);
        if (layoutDir != LayoutConverter.Direction.None) SwitchSystemLayout(layoutDir);

        // Синхронизируем буфер с тем, что теперь на экране.
        _buffer.Clear();
        foreach (var ch in corrected) _buffer.Append(ch);
        _buffer.Append(sep);

        // Запоминаем коррекцию — для возможного отката пользователем.
        _lastAutoCorrection = new AutoCorrection(word, corrected, sep, DateTime.UtcNow, layoutDir);
    }

    /// <summary>
    /// Откат последней автокоррекции: на экране сейчас "corrected + sep",
    /// стираем это и печатаем "original + sep". Раскладку возвращаем обратно.
    /// </summary>
    private void UndoLastAutoCorrection()
    {
        var lc = _lastAutoCorrection;
        if (lc == null) return;
        _lastAutoCorrection = null;
        UndoCorrection(lc);
    }

    private void UndoCorrection(AutoCorrection lc)
    {
        Sender.ReleaseHotkeyModifiers();
        Sender.SendBackspaces(lc.Corrected.Length + 1);
        ClipboardPaste.Paste(lc.Original + lc.Separator);
        // Раскладку возвращаем обратно.
        if (lc.Dir == LayoutConverter.Direction.ToRu) SwitchSystemLayout(LayoutConverter.Direction.ToEn);
        else if (lc.Dir == LayoutConverter.Direction.ToEn) SwitchSystemLayout(LayoutConverter.Direction.ToRu);

        _buffer.Clear();
        foreach (var ch in lc.Original) _buffer.Append(ch);
        _buffer.Append(lc.Separator);
    }

    private void RunConvert()
    {
        // Приоритет — буферный режим: то, что пользователь только что набрал в текущей сессии.
        // Если буфер пуст, пробуем выделенный текст (через Ctrl+C round-trip).
        var buffered = _buffer.Snapshot();
        if (buffered.Length == 0)
        {
            SelectionConverter.TryConvertSelection();
            return;
        }

        var (converted, dir) = LayoutConverter.AutoConvertWithDirection(buffered);
        if (converted != buffered)
        {
            int tailAfterCursor = buffered.Length - _buffer.CursorPosition;
            Sender.ReleaseHotkeyModifiers();
            // 1) Довозим реальную каретку до конца буфера (если курсор был в середине после правок).
            if (tailAfterCursor > 0) Sender.SendRightArrow(tailAfterCursor);
            // 2) Стираем буфер бэкспейсами (надёжно во всех приложениях, включая UWP).
            Sender.SendBackspaces(buffered.Length);
            // 3) Вставляем конвертированный текст одним Ctrl+V из clipboard — атомарно, без посимвольного ввода.
            ClipboardPaste.Paste(converted);
            SwitchSystemLayout(dir);
        }
        _buffer.Clear();
    }

    private static void SwitchSystemLayout(LayoutConverter.Direction dir)
    {
        if (dir == LayoutConverter.Direction.ToRu) LayoutSwitcher.SwitchToRussian();
        else if (dir == LayoutConverter.Direction.ToEn) LayoutSwitcher.SwitchToEnglish();
    }

    public void Dispose()
    {
        _kbHook.Dispose();
        _mouseHook.Dispose();
        _fgWatcher.Dispose();
    }
}
