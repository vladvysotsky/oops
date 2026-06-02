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
    private readonly KeyboardHook _kbHook = new();
    private readonly MouseHook _mouseHook = new();
    private readonly ForegroundWatcher _fgWatcher = new();

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

            // 4) Авто-определение: на разделителе анализируем только что завершённое слово.
            if (Settings.AutoDetectWrongLayout && IsWordSeparator(c))
                _uiContext.Post(_ => TryAutoConvertLastWord(), null);
        }
    }

    private static bool IsWordSeparator(char c) =>
        c == ' ' || c == '\t' || c == ',' || c == '.' || c == ';' || c == ':'
        || c == '!' || c == '?' || c == '/' || c == '\\' || c == '"' || c == '\''
        || c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}';

    private void TryAutoConvertLastWord()
    {
        // Берём буфер; последний символ — только что вставленный разделитель.
        var snap = _buffer.Snapshot();
        if (snap.Length < 2) return;
        var sep = snap[^1];

        // Ищем границу предыдущего слова.
        int wordEnd = snap.Length - 1;          // индекс разделителя
        int wordStart = wordEnd;
        while (wordStart > 0 && !IsWordSeparator(snap[wordStart - 1])) wordStart--;
        var word = snap.Substring(wordStart, wordEnd - wordStart);

        var verdict = AutoDetector.Analyze(word);
        if (verdict == AutoDetector.Verdict.Keep) return;

        var converted = verdict == AutoDetector.Verdict.WasMeantRussian
            ? LayoutConverter.ToRussian(word)
            : LayoutConverter.ToEnglish(word);
        if (converted == word) return;

        // Заменяем слово в активном поле: стираем (word.Length + 1) символов
        // (само слово плюс разделитель), потом печатаем converted + sep.
        Sender.ReleaseHotkeyModifiers();
        Sender.SendBackspaces(word.Length + 1);
        ClipboardPaste.Paste(converted + sep);

        SwitchSystemLayout(verdict == AutoDetector.Verdict.WasMeantRussian
            ? LayoutConverter.Direction.ToRu : LayoutConverter.Direction.ToEn);

        // Синхронизируем буфер с тем, что теперь на экране.
        _buffer.Clear();
        foreach (var ch in converted) _buffer.Append(ch);
        _buffer.Append(sep);
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
