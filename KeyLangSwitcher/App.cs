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
        _fgWatcher.ForegroundChanged += (_, _) => _buffer.Clear();

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
            _buffer.Append(e.TypedChar.Value);
    }

    private void RunConvert()
    {
        // 1) Сначала всегда пробуем выделение — если в активном окне что-то выделено,
        //    это явное намерение пользователя сконвертировать именно его.
        if (SelectionConverter.TryConvertSelection())
        {
            _buffer.Clear();
            return;
        }

        // 2) Иначе — буферный режим: стираем N символов и печатаем новые.
        var buffered = _buffer.Snapshot();
        if (buffered.Length == 0) return;

        var (converted, dir) = LayoutConverter.AutoConvertWithDirection(buffered);
        if (converted != buffered)
        {
            int tailAfterCursor = buffered.Length - _buffer.CursorPosition;
            Sender.ReleaseHotkeyModifiers();
            // 1) Довозим реальную каретку до конца буфера (если курсор был в середине после правок).
            if (tailAfterCursor > 0) Sender.SendRightArrow(tailAfterCursor);
            // 2) Выделяем весь набранный текст справа налево, кладём конвертированный в clipboard и вставляем.
            //    Замена визуально мгновенная (одно событие paste), не зависит от частоты обновления приёмника.
            ClipboardReplace.ReplaceLastN(buffered.Length, converted);
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
