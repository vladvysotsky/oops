using KeyLangSwitcher.Core;
using KeyLangSwitcher.Hooks;
using KeyLangSwitcher.Settings;

namespace KeyLangSwitcher;

/// <summary>
/// Главный координатор. МАКСИМАЛЬНО ПРОСТАЯ модель:
///   - работает ТОЛЬКО с ВЫДЕЛЕННЫМ текстом;
///   - никакого буфера набранного текста, никаких словарей и автокоррекции;
///   - хоткей конвертации: выделение → 1-в-1 смена раскладки + переключение системной раскладки;
///   - хоткей смены регистра: выделение → toggle upper/lower.
/// Результат печатается напрямую (SendUnicode), заменяя выделение. Clipboard
/// используется только для ЧТЕНИЯ выделения (Ctrl+C) и сразу восстанавливается —
/// в историю Win+V наш конвертированный текст не попадает.
/// </summary>
public sealed class App : IDisposable
{
    public AppSettings Settings { get; }
    private readonly KeyboardHook _kbHook = new();
    private readonly SynchronizationContext _uiContext;

    public App(AppSettings settings)
    {
        Settings = settings;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("App must be created on the UI thread");

        _kbHook.KeyDown += OnKeyDown;
        _kbHook.Install();
    }

    public void ApplySettings() { /* нет состояния для применения */ }

    private void OnKeyDown(object? sender, KeyboardHook.KeyEvent e)
    {
        if (!Settings.Enabled) return;

        // Хоткей конвертации раскладки выделенного текста.
        if (Settings.ConvertHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            _uiContext.Post(_ => SelectionConverter.ConvertSelection(), null);
            return;
        }

        // Хоткей смены регистра выделенного текста.
        if (Settings.ChangeCaseHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            _uiContext.Post(_ => SelectionConverter.ToggleSelectionCase(), null);
            return;
        }
    }

    public void Dispose() => _kbHook.Dispose();
}
