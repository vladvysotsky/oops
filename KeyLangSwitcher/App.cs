using System.Windows.Forms;
using KeyLangSwitcher.Core;
using KeyLangSwitcher.Hooks;
using KeyLangSwitcher.Settings;

namespace KeyLangSwitcher;

/// <summary>
/// Главный координатор.
///
/// Модель работы — «расширяющаяся область», без единой догадки и БЕЗ буфера обмена:
///   - приложение ведёт собственную ленту набранных символов (TypingBuffer);
///   - 1-е нажатие хоткея правит последнее слово, 2-е — два последних, 3-е — три…;
///   - каждый шаг это преобразование 1-в-1 чётко очерченного куска;
///   - текст переписывается через Backspace + SendInput(Unicode), clipboard не трогаем вообще.
///
/// Два хоткея с одинаковой логикой области: смена раскладки и смена регистра.
/// </summary>
public sealed class App : IDisposable
{
    public AppSettings Settings { get; }

    private readonly TypingBuffer _buffer = new();
    private readonly ScopeEditor _scope = new();
    private readonly LayoutTracker _layoutTracker = new();
    private readonly KeyboardHook _kbHook = new();
    private readonly MouseHook _mouseHook = new();
    private readonly ForegroundWatcher _fgWatcher = new();

    private readonly SynchronizationContext _uiContext;

    public App(AppSettings settings)
    {
        Settings = settings;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("App must be created on the UI thread");

        ApplySettings();

        _kbHook.KeyDown += OnKeyDown;
        _mouseHook.Clicked += (_, _) => ResetAll();
        _fgWatcher.ForegroundChanged += (_, _) =>
        {
            ResetAll();
            _layoutTracker.Reset();
        };

        _kbHook.Install();
        _mouseHook.Install();
        _fgWatcher.Install();
    }

    public void ApplySettings()
    {
        _buffer.IdleTimeout = TimeSpan.FromSeconds(Settings.BufferIdleTimeoutSeconds);
        _scope.ExpandWindow = TimeSpan.FromSeconds(Settings.ExpandWindowSeconds);
    }

    private void ResetAll()
    {
        _buffer.Clear();
        _scope.ResetSession();
    }

    private void OnKeyDown(object? sender, KeyboardHook.KeyEvent e)
    {
        if (!Settings.Enabled) return;

        // Пользователь сам сменил раскладку (Alt+Shift, Win+Space) — дальнейшие
        // нажатия дают другие буквы, наша лента больше не соответствует экрану.
        if (_layoutTracker.LayoutChangedSinceLastCheck())
            ResetAll();

        if (Settings.ConvertHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            // Пока Alt ещё зажат — гасим активацию строки меню, иначе уедет фокус.
            Sender.CancelAltMenuActivation();
            _uiContext.Post(_ => RunStep(layout: true), null);
            return;
        }

        if (Settings.ChangeCaseHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            Sender.CancelAltMenuActivation();
            _uiContext.Post(_ => RunStep(layout: false), null);
            return;
        }

        switch (e.VirtualKey)
        {
            case Keys.Back:
                _buffer.Backspace();
                _scope.ResetSession();
                return;

            // Любая навигация и завершение ввода — мы теряем связь с экраном.
            case Keys.Delete:
            case Keys.Left:
            case Keys.Right:
            case Keys.Home:
            case Keys.End:
            case Keys.Up:
            case Keys.Down:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Enter:
            case Keys.Tab:
            case Keys.Escape:
                ResetAll();
                return;
        }

        if (e.TypedChar.HasValue)
        {
            _buffer.Append(e.TypedChar.Value);
            _scope.ResetSession(); // новый ввод прерывает расширение области
        }
    }

    private void RunStep(bool layout)
    {
        var text = _buffer.Snapshot();
        if (text.Length == 0) return;

        var edit = layout
            ? _scope.NextLayoutStep(text, DateTime.UtcNow)
            : _scope.NextCaseStep(text, DateTime.UtcNow);

        if (edit.IsEmpty) return;

        // Ждём, пока пользователь отпустит модификаторы хоткея: иначе зажатый Ctrl
        // превратит наши Backspace в Ctrl+Backspace (удаление слова целиком).
        Sender.WaitForModifiersReleased();
        Sender.ReleaseHotkeyModifiers();

        Sender.SendBackspaces(edit.EraseCount);
        Sender.SendUnicode(edit.Text);

        if (edit.Direction == LayoutConverter.Direction.ToRu) LayoutSwitcher.SwitchToRussian();
        else if (edit.Direction == LayoutConverter.Direction.ToEn) LayoutSwitcher.SwitchToEnglish();

        // Лента теперь соответствует тому, что на экране.
        _buffer.Reset(edit.NewBufferContent);
    }

    public void Dispose()
    {
        _kbHook.Dispose();
        _mouseHook.Dispose();
        _fgWatcher.Dispose();
    }
}
