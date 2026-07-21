using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyLangSwitcher.Core;
using KeyLangSwitcher.Settings;

namespace KeyLangSwitcher.UI;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly CheckBox _cbEnabled = new() { Text = "Включено", AutoSize = true };
    private readonly CheckBox _cbAutostart = new() { Text = "Запускать при старте Windows", AutoSize = true };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Width = 180, Margin = new Padding(0, 0, 6, 0) };
    private readonly Button _btnRecord = new() { Text = "Записать...", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new System.Drawing.Size(110, 28), Padding = new Padding(6, 2, 6, 2) };
    private readonly TextBox _caseHotkeyBox = new() { ReadOnly = true, Width = 180, Margin = new Padding(0, 0, 6, 0) };
    private readonly Button _btnRecordCase = new() { Text = "Записать...", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new System.Drawing.Size(110, 28), Padding = new Padding(6, 2, 6, 2) };
    private readonly Button _btnSave = new() { Text = "Сохранить", DialogResult = DialogResult.OK, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new System.Drawing.Size(110, 30), Padding = new Padding(10, 4, 10, 4) };
    private readonly Button _btnCancel = new() { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new System.Drawing.Size(90, 30), Padding = new Padding(10, 4, 10, 4) };

    private HotkeyConfig _hotkey;
    private HotkeyConfig _caseHotkey;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        _hotkey = Clone(settings.ConvertHotkey);
        _caseHotkey = Clone(settings.ChangeCaseHotkey);

        Text = "KeyLangSwitcher — настройки";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(620, 300);
        MinimumSize = new System.Drawing.Size(620, 300);

        // --- Buttons row (docked bottom). Add BEFORE the content panel so Fill respects it. ---
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(12),
            AutoSize = false,
        };
        _btnSave.Margin = new Padding(6, 0, 0, 0);
        _btnCancel.Margin = new Padding(6, 0, 0, 0);
        buttons.Controls.Add(_btnCancel);
        buttons.Controls.Add(_btnSave);
        Controls.Add(buttons);

        // --- Content ---
        var lblHotkey = new Label { Text = "Хоткей конвертации:", AutoSize = true, MinimumSize = new System.Drawing.Size(220, 0), TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 8, 0) };
        var lblCaseHotkey = new Label { Text = "Хоткей смены регистра:", AutoSize = true, MinimumSize = new System.Drawing.Size(220, 0), TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 8, 0) };

        var tooltip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 200, ShowAlways = true };
        tooltip.SetToolTip(_cbEnabled, "Глобально включает / выключает работу хоткеев.");
        tooltip.SetToolTip(_cbAutostart, "Прописать запуск программы в реестр HKCU\\...\\Run.");
        tooltip.SetToolTip(_hotkeyBox,
            "Конвертирует раскладку ВЫДЕЛЕННОГО текста (1-в-1) и переключает\n" +
            "системную раскладку. Жми \"Записать...\" чтобы сменить комбинацию.");
        tooltip.SetToolTip(_caseHotkeyBox,
            "Меняет регистр ВЫДЕЛЕННОГО текста.\n" +
            "Если есть хоть одна заглавная — всё в нижний, иначе всё в верхний.");
        _btnRecord.Click += (_, _) => _hotkey = RecordHotkey(_hotkey, _hotkeyBox) ?? _hotkey;
        _btnRecordCase.Click += (_, _) => _caseHotkey = RecordHotkey(_caseHotkey, _caseHotkeyBox) ?? _caseHotkey;

        // Каждая строка — своя горизонтальная FlowLayoutPanel. Никакого TableLayoutPanel,
        // никаких неожиданных выравниваний между ячейками разной высоты.
        Panel Row(params Control[] children)
        {
            var p = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 6),
            };
            foreach (var c in children)
            {
                if (c is Label) c.Anchor = AnchorStyles.Left;
                p.Controls.Add(c);
            }
            return p;
        }

        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = false,
            Padding = new Padding(16),
        };

        content.Controls.Add(_cbEnabled);
        content.Controls.Add(_cbAutostart);
        content.Controls.Add(Row(lblHotkey, _hotkeyBox, _btnRecord));
        content.Controls.Add(Row(lblCaseHotkey, _caseHotkeyBox, _btnRecordCase));

        Controls.Add(content);
        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        // populate
        _cbEnabled.Checked = settings.Enabled;
        _cbAutostart.Checked = settings.Autostart;
        _hotkeyBox.Text = _hotkey.ToString();
        _caseHotkeyBox.Text = _caseHotkey.ToString();

        _btnSave.Click += (_, _) => ApplyToSettings();
    }

    /// <summary>
    /// Открывает диалог записи; возвращает записанную комбинацию или null если отменили.
    /// Также синхронизирует текст в указанном поле.
    /// </summary>
    private HotkeyConfig? RecordHotkey(HotkeyConfig current, TextBox display)
    {
        using var dlg = new HotkeyRecordDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
        {
            display.Text = dlg.Result.ToString();
            return dlg.Result;
        }
        return null;
    }

    private static HotkeyConfig Clone(HotkeyConfig h) => new()
    {
        Ctrl = h.Ctrl, Shift = h.Shift, Alt = h.Alt, Win = h.Win, Key = h.Key,
    };

    private void ApplyToSettings()
    {
        _settings.Enabled = _cbEnabled.Checked;
        _settings.Autostart = _cbAutostart.Checked;
        _settings.ConvertHotkey = _hotkey;
        _settings.ChangeCaseHotkey = _caseHotkey;
    }
}

/// <summary>
/// Запись хоткея: накапливаем нажатые клавиши, фиксируем по отпусканию всех.
/// Win-клавиша определяется через GetAsyncKeyState (WinForms её не видит в Modifiers).
/// </summary>
public sealed class HotkeyRecordDialog : Form
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;

    public HotkeyConfig? Result { get; private set; }
    private readonly Label _label = new() { AutoSize = true };

    private bool _ctrl, _shift, _alt, _win;
    private int _key;
    private bool _anyPressed;

    public HotkeyRecordDialog()
    {
        Text = "Запись хоткея";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new System.Drawing.Size(360, 110);
        KeyPreview = true;
        _label.Text = "Нажмите комбинацию и отпустите...";
        _label.Location = new System.Drawing.Point(20, 30);
        Controls.Add(_label);

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    private static bool IsModifier(Keys k) =>
        k is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
          or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
          or Keys.Menu or Keys.LMenu or Keys.RMenu
          or Keys.LWin or Keys.RWin;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        _anyPressed = true;
        if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0)
            _win = true;
        if (e.Control) _ctrl = true;
        if (e.Shift)   _shift = true;
        if (e.Alt)     _alt = true;
        if (!IsModifier(e.KeyCode))
            _key = (int)e.KeyCode;

        UpdateLabel();
        e.Handled = true; e.SuppressKeyPress = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_anyPressed) return;
        // Когда все клавиши отпущены — фиксируем.
        bool stillDown =
            (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        if (stillDown) return;
        if (!_ctrl && !_shift && !_alt && !_win && _key == 0) return;

        Result = new HotkeyConfig { Ctrl = _ctrl, Shift = _shift, Alt = _alt, Win = _win, Key = _key };
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateLabel()
    {
        var preview = new HotkeyConfig { Ctrl = _ctrl, Shift = _shift, Alt = _alt, Win = _win, Key = _key };
        _label.Text = preview.ToString();
    }
}
