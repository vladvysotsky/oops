using System.Windows.Forms;
using Oops.Core;
using Oops.Hooks;
using Oops.Settings;

namespace Oops;

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
        // Наши собственные переключения после конвертации сюда не попадают.
        if (_layoutTracker.UserChangedLayout())
            ResetAll();

        // Момент нажатия фиксируем здесь, а не внутри RunStep: там мы сперва ждём,
        // пока пользователь отпустит модификаторы, и печатаем текст посимвольно.
        // Окно расширения должно мерить ритм нажатий пользователя, а не нашу
        // собственную задержку, иначе на длинных словах область перестаёт расти.
        var pressedAtUtc = DateTime.UtcNow;

        if (Settings.ConvertHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            // Автоповтор удерживаемой клавиши — не новое нажатие. Без этой проверки
            // одно удержание Ctrl+Win даёт десятки шагов подряд.
            if (e.IsRepeat) return;
            // Пока Alt ещё зажат — гасим активацию строки меню, иначе уедет фокус.
            Sender.CancelAltMenuActivation();
            _uiContext.Post(_ => RunStep(layout: true, pressedAtUtc), null);
            return;
        }

        if (Settings.ChangeCaseHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            Sender.CancelAltMenuActivation();
            _uiContext.Post(_ => RunStep(layout: false, pressedAtUtc), null);
            return;
        }

        // Любое сочетание с Ctrl или Alt — это команда приложению (Ctrl+A, Ctrl+Z,
        // Ctrl+V, Ctrl+X…), она может изменить текст как угодно, и наша лента
        // перестаёт отражать экран. Особенно важен Ctrl+A: без сброса лента
        // осталась бы непустой, мы пошли бы в режим области, и первый же Backspace
        // удалил бы всё выделение целиком, а следом напечаталось бы одно слово.
        //
        // Сами модификаторы при этом не сбрасывают: наши хоткеи modifier-only
        // (Ctrl+Win, Alt+Win) срабатывают именно на нажатие модификатора, и сброс
        // по голому Ctrl убил бы их до того, как нажат Win.
        if ((e.Ctrl || e.Alt) && !IsModifierKey(e.VirtualKey))
        {
            ResetAll();
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

    private static bool IsModifierKey(Keys k) =>
        k is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
          or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
          or Keys.Menu or Keys.LMenu or Keys.RMenu
          or Keys.LWin or Keys.RWin;

    private void RunStep(bool layout, DateTime pressedAtUtc)
    {
        // Ждём, пока пользователь отпустит модификаторы хоткея: иначе зажатый Ctrl
        // превратит наши Backspace в Ctrl+Backspace (удаление слова целиком),
        // а Ctrl+C для чтения выделения уйдёт как Ctrl+Alt+C и не сработает.
        Sender.WaitForModifiersReleased();
        Sender.ReleaseHotkeyModifiers();

        // Если лента не пуста — пользователь только что печатал, работаем по ней.
        // Выделение пробуем только когда лента пуста, и это не компромисс, а
        // следствие: выделить текст можно либо мышью, либо Shift+стрелками, а оба
        // действия ленту очищают. То есть «есть выделение» ⇒ «лента пуста».
        //
        // Порядок важен ещё и потому, что в новом Notepad (UWP) Ctrl+C без выделения
        // копирует всю текущую строку — проба выделения при непустой ленте
        // приняла бы эту строку за выделение и переписала бы её целиком.
        if (_buffer.Length > 0) ConvertBufferScope(layout, pressedAtUtc);
        else TryConvertSelection(layout);
    }

    /// <summary>
    /// Если в активном окне есть выделение — преобразует его целиком и печатает
    /// поверх (выделение ещё активно, ввод его перетирает). Возвращает false,
    /// если выделения нет и нужно работать с набранным буфером.
    /// </summary>
    private bool TryConvertSelection(bool layout)
    {
        var selection = SelectionReader.TryRead();
        if (string.IsNullOrEmpty(selection)) return false;

        string converted;
        var dir = LayoutConverter.Direction.None;
        if (layout) (converted, dir) = LayoutConverter.AutoConvertWithDirection(selection);
        else converted = ScopeEditor.ToggleCase(selection);

        if (converted != selection)
        {
            Sender.SendUnicode(converted);
            SwitchSystemLayout(dir);
        }

        // Мы переписали не то, что вели в ленте — она больше не отражает экран.
        ResetAll();
        return true;
    }

    /// <summary>Шаг расширяющейся области по набранному тексту.</summary>
    private void ConvertBufferScope(bool layout, DateTime pressedAtUtc)
    {
        var text = _buffer.Snapshot();
        if (text.Length == 0) return;

        var edit = layout
            ? _scope.NextLayoutStep(text, pressedAtUtc)
            : _scope.NextCaseStep(text, pressedAtUtc);

        if (edit.IsEmpty) return;

        Sender.SendBackspaces(edit.EraseCount);
        Sender.SendUnicode(edit.Text);

        SwitchSystemLayout(edit.Direction);

        // Лента теперь соответствует тому, что на экране.
        _buffer.Reset(edit.NewBufferContent);
    }

    /// <summary>
    /// Переключает системную раскладку и помечает смену как нашу, чтобы
    /// LayoutTracker не принял её за ручное переключение пользователем
    /// и не сбросил ленту — иначе следующее нажатие хоткея не смогло бы
    /// расширить область.
    /// </summary>
    private void SwitchSystemLayout(LayoutConverter.Direction dir)
    {
        if (dir == LayoutConverter.Direction.None) return;

        _layoutTracker.NoteSelfSwitch();
        if (dir == LayoutConverter.Direction.ToRu) LayoutSwitcher.SwitchToRussian();
        else LayoutSwitcher.SwitchToEnglish();
    }

    public void Dispose()
    {
        _kbHook.Dispose();
        _mouseHook.Dispose();
        _fgWatcher.Dispose();
    }
}
