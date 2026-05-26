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
    private readonly TextBox _hotkeyBox = new() { ReadOnly = true, Width = 220 };
    private readonly Button _btnSave = new() { Text = "Сохранить", DialogResult = DialogResult.OK };
    private readonly Button _btnCancel = new() { Text = "Отмена", DialogResult = DialogResult.Cancel };

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
        ClientSize = new System.Drawing.Size(440, 280);

        var lblHotkey = new Label { Text = "Хоткей конвертации:", AutoSize = true };
        var btnRecord = new Button { Text = "Записать..." , Width = 100 };
        btnRecord.Click += (_, _) => RecordHotkey();

        var lblIdle = new Label { Text = "Сброс буфера через (сек):", AutoSize = true };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        layout.Controls.Add(_cbEnabled, 0, 0); layout.SetColumnSpan(_cbEnabled, 2);
        layout.Controls.Add(_cbAutostart, 0, 1); layout.SetColumnSpan(_cbAutostart, 2);
        layout.Controls.Add(_cbAutoDetect, 0, 2); layout.SetColumnSpan(_cbAutoDetect, 2);

        layout.Controls.Add(lblHotkey, 0, 3);
        var hotkeyPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        hotkeyPanel.Controls.Add(_hotkeyBox);
        hotkeyPanel.Controls.Add(btnRecord);
        layout.Controls.Add(hotkeyPanel, 1, 3);

        layout.Controls.Add(lblIdle, 0, 4);
        layout.Controls.Add(_nudIdle, 1, 4);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(_btnCancel);
        buttons.Controls.Add(_btnSave);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        // Заполняем поля
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

/// <summary>Простой диалог записи хоткея — слушает KeyDown и сохраняет комбинацию.</summary>
public sealed class HotkeyRecordDialog : Form
{
    public HotkeyConfig? Result { get; private set; }
    private readonly Label _label = new() { Text = "Нажмите комбинацию...", AutoSize = true };

    public HotkeyRecordDialog()
    {
        Text = "Запись хоткея";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new System.Drawing.Size(320, 100);
        KeyPreview = true;
        _label.Location = new System.Drawing.Point(20, 30);
        Controls.Add(_label);

        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.KeyCode;
        // Игнорируем "только модификаторы" в виде основной клавиши.
        bool isModifierOnly = key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
                                  or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
                                  or Keys.Menu or Keys.LMenu or Keys.RMenu
                                  or Keys.LWin or Keys.RWin;

        var cfg = new HotkeyConfig
        {
            Ctrl = e.Control,
            Shift = e.Shift,
            Alt = e.Alt,
            // WinForms KeyEventArgs не даёт Win напрямую — для записи мы пользуемся
            // тем, что Win-флаг в Modifiers не виден. Считаем, что Win нажат, если используется Apps/LWin/RWin —
            // здесь приближение, но для большинства комбинаций достаточно.
            Win = key is Keys.LWin or Keys.RWin,
            Key = isModifierOnly ? 0 : (int)key,
        };

        if (!cfg.Ctrl && !cfg.Shift && !cfg.Alt && !cfg.Win && cfg.Key == 0)
            return;

        Result = cfg;
        _label.Text = cfg.ToString();
        e.Handled = true; e.SuppressKeyPress = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
