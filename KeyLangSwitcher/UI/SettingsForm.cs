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
    private readonly CheckBox _cbAutoDetect = new() { Text = "Автоматически исправлять раскладку (бета)", AutoSize = true };
    private readonly NumericUpDown _nudIdle = new() { Minimum = 5, Maximum = 600, Value = 30, Width = 80 };
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Width = 180 };
    private readonly Button _btnRecord = new() { Text = "Записать...", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new System.Drawing.Size(110, 28), Padding = new Padding(6, 2, 6, 2) };
    private readonly Button _btnSave = new() { Text = "Сохранить", DialogResult = DialogResult.OK, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new System.Drawing.Size(110, 30), Padding = new Padding(10, 4, 10, 4) };
    private readonly Button _btnCancel = new() { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new System.Drawing.Size(90, 30), Padding = new Padding(10, 4, 10, 4) };

    private HotkeyConfig _hotkey;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        _hotkey = new HotkeyConfig
        {
            Ctrl = settings.ConvertHotkey.Ctrl,
            Shift = settings.ConvertHotkey.Shift,
            Alt = settings.ConvertHotkey.Alt,
            Win = settings.ConvertHotkey.Win,
            Key = settings.ConvertHotkey.Key,
        };

        Text = "KeyLangSwitcher — настройки";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(580, 360);
        MinimumSize = new System.Drawing.Size(580, 360);

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
        var lblHotkey = new Label { Text = "Хоткей конвертации:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
        var lblIdle   = new Label { Text = "Забывать набранное через (сек):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
        _nudIdle.Margin = new Padding(3, 6, 3, 3);

        var tooltip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 200, ShowAlways = true };
        tooltip.SetToolTip(lblIdle,
            "Если в течение N секунд ничего не печатать, накопленный текст\n" +
            "перестаёт быть кандидатом на конвертацию по хоткею. Это нужно,\n" +
            "чтобы при возврате к окну через минуту хоткей не пытался\n" +
            "переписать давно забытый ввод.");
        tooltip.SetToolTip(_nudIdle, tooltip.GetToolTip(lblIdle));
        tooltip.SetToolTip(_cbAutoDetect,
            "Бета: пытается сама поправить раскладку, как только распознает\n" +
            "слово, набранное не в той раскладке. Пока не реализовано.");
        tooltip.SetToolTip(_cbEnabled, "Глобально включает / выключает работу хоткея и буфера.");
        tooltip.SetToolTip(_cbAutostart, "Прописать запуск программы в реестр HKCU\\...\\Run.");
        tooltip.SetToolTip(_hotkeyBox, "Текущая комбинация. Жми \"Записать...\" чтобы сменить.");
        _btnRecord.Click += (_, _) => RecordHotkey();

        var hotkeyPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
        };
        hotkeyPanel.Controls.Add(_hotkeyBox);
        hotkeyPanel.Controls.Add(_btnRecord);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(14),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        layout.Controls.Add(_cbEnabled,    0, row); layout.SetColumnSpan(_cbEnabled, 2);    row++;
        layout.Controls.Add(_cbAutostart,  0, row); layout.SetColumnSpan(_cbAutostart, 2);  row++;
        layout.Controls.Add(_cbAutoDetect, 0, row); layout.SetColumnSpan(_cbAutoDetect, 2); row++;
        layout.Controls.Add(lblHotkey,     0, row);
        layout.Controls.Add(hotkeyPanel,   1, row); row++;
        layout.Controls.Add(lblIdle,       0, row);
        layout.Controls.Add(_nudIdle,      1, row); row++;

        Controls.Add(layout);
        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        // populate
        _cbEnabled.Checked = settings.Enabled;
        _cbAutostart.Checked = settings.Autostart;
        _cbAutoDetect.Checked = settings.AutoDetectWrongLayout;
        _nudIdle.Value = settings.BufferIdleTimeoutSeconds;
        _hotkeyBox.Text = _hotkey.ToString();

        _btnSave.Click += (_, _) => ApplyToSettings();
    }

    private void RecordHotkey()
    {
        using var dlg = new HotkeyRecordDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
        {
            _hotkey = dlg.Result;
            _hotkeyBox.Text = _hotkey.ToString();
        }
    }

    private void ApplyToSettings()
    {
        _settings.Enabled = _cbEnabled.Checked;
        _settings.Autostart = _cbAutostart.Checked;
        _settings.AutoDetectWrongLayout = _cbAutoDetect.Checked;
        _settings.BufferIdleTimeoutSeconds = (int)_nudIdle.Value;
        _settings.ConvertHotkey = _hotkey;
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
