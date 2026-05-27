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
    public event EventHandler<string>? HotkeyFired;
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
            System.Diagnostics.Debug.WriteLine($"[hotkey] matched on {e.VirtualKey} ctrl={e.Ctrl} alt={e.Alt} shift={e.Shift} win={e.Win}");
            // Глотаем событие, чтобы оно не дошло до приложения
            e.Handled = true;
            // Визуальный фидбек (balloon в трее)
            var info = $"match {e.VirtualKey} buf={_buffer.Length}";
            _uiContext.Post(_ => HotkeyFired?.Invoke(this, info), null);

            // Выполняем в UI-потоке — нужен для clipboard
            _uiContext.Post(_ => RunConvert(), null);
            return;
        }

        // 2) Сброс буфера на спец-клавишах
        if (IsBufferResetKey(e.VirtualKey))
        {
            _buffer.Clear();
            return;
        }

        // 3) Backspace
        if (e.VirtualKey == Keys.Back)
        {
            _buffer.Backspace();
            return;
        }

        // 4) Накопление символов
        if (e.TypedChar.HasValue)
            _buffer.Append(e.TypedChar.Value);
    }

    private static bool IsBufferResetKey(Keys k) => k is
        Keys.Enter or Keys.Return or Keys.Tab or Keys.Escape
        or Keys.Up or Keys.Down or Keys.Left or Keys.Right
        or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;

    private void RunConvert()
    {
        // 1) Сначала всегда пробуем выделение — если в активном окне что-то выделено,
        //    это явное намерение пользователя сконвертировать именно его.
        if (SelectionConverter.TryConvertSelection())
        {
            _buffer.Clear();
            return;
        }

        // 2) Иначе — буферный режим: заменяем последние N символов через clipboard.
        var buffered = _buffer.Snapshot();
        if (buffered.Length == 0) return;

        var (converted, dir) = LayoutConverter.AutoConvertWithDirection(buffered);
        if (converted != buffered)
        {
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
