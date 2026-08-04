using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyLangSwitcher.Core;
using KeyLangSwitcher.Settings;

namespace KeyLangSwitcher.UI;

/// <summary>
/// Окно настроек. Построено по дизайн-системе из <see cref="Theme"/>:
/// секции-карточки, 8px-сетка, один акцентный цвет, хоткеи показаны «клавишами».
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private readonly CheckBox _cbEnabled = new();
    private readonly CheckBox _cbAutostart = new();
    private readonly HotkeyDisplay _convertKeys = new();
    private readonly HotkeyDisplay _caseKeys = new();
    private readonly NumericUpDown _nudIdle = new();
    private readonly NumericUpDown _nudExpand = new();

    private HotkeyConfig _convertHotkey;
    private HotkeyConfig _caseHotkey;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        _convertHotkey = Clone(settings.ConvertHotkey);
        _caseHotkey = Clone(settings.ChangeCaseHotkey);

        Text = "KeyLangSwitcher";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Canvas;
        Font = Theme.Body;
        ClientSize = new Size(560, 620);

        BuildLayout();
        Populate();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4, Theme.S4, Theme.S4, Theme.S2),
        };

        content.Controls.Add(Header());
        content.Controls.Add(SectionLabel("ОБЩИЕ"));
        content.Controls.Add(GeneralCard());
        content.Controls.Add(SectionLabel("ГОРЯЧИЕ КЛАВИШИ"));
        content.Controls.Add(HotkeysCard());
        content.Controls.Add(SectionLabel("ПОВЕДЕНИЕ"));
        content.Controls.Add(BehaviourCard());

        // Явная сетка вместо Dock.Fill + Dock.Bottom: порядок докинга в WinForms
        // зависит от z-order и легко ломается при правках.
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Canvas,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        grid.Controls.Add(content, 0, 0);
        grid.Controls.Add(Footer(), 0, 1);

        Controls.Add(grid);
    }

    private Control Header()
    {
        var panel = new Panel
        {
            Width = CardWidth,
            Height = 64,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, 0, 0, Theme.S3),
        };

        var title = new Label
        {
            Text = "KeyLangSwitcher",
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(0, 0),
            BackColor = Color.Transparent,
        };
        var subtitle = new Label
        {
            Text = "Правит раскладку и регистр только что набранного текста.\n"
                 + "Границу задаёте вы: каждое следующее нажатие захватывает ещё одно слово.",
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(CardWidth, 0),
            Location = new Point(0, 26),
            BackColor = Color.Transparent,
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        return panel;
    }

    private static Control SectionLabel(string text) => new Label
    {
        Text = text,
        Font = Theme.SectionLabel,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        Margin = new Padding(Theme.S1, Theme.S3, 0, Theme.S2),
        BackColor = Color.Transparent,
    };

    private const int CardWidth = 496;

    private Control GeneralCard()
    {
        var card = new Card { Width = CardWidth, Height = 116, Margin = new Padding(0) };
        var rows = StackInside(card);
        rows.Controls.Add(CheckRow(_cbEnabled, "Включено",
            "Глобально включает и выключает горячие клавиши."));
        rows.Controls.Add(Divider());
        rows.Controls.Add(CheckRow(_cbAutostart, "Запускать при старте Windows",
            "Запись в реестр HKCU\\…\\Run."));
        return card;
    }

    private Control HotkeysCard()
    {
        var card = new Card { Width = CardWidth, Height = 172, Margin = new Padding(0) };
        var rows = StackInside(card);
        rows.Controls.Add(HotkeyRow(
            "Раскладка",
            "Меняет RU ↔ EN у последнего слова. Ещё нажатие — ещё слово.",
            _convertKeys,
            () => RecordInto(ref _convertHotkey, _convertKeys)));
        rows.Controls.Add(Divider());
        rows.Controls.Add(HotkeyRow(
            "Регистр",
            "ВЕРХНИЙ ↔ нижний по той же логике области.",
            _caseKeys,
            () => RecordInto(ref _caseHotkey, _caseKeys)));
        return card;
    }

    private Control BehaviourCard()
    {
        var card = new Card { Width = CardWidth, Height = 168, Margin = new Padding(0) };
        var rows = StackInside(card);

        _nudExpand.Minimum = 1; _nudExpand.Maximum = 10;
        rows.Controls.Add(NumberRow(_nudExpand, "Окно расширения", "сек",
            "Столько времени следующее нажатие продолжает расширять ту же область,\nа не начинает новую."));
        rows.Controls.Add(Divider());

        _nudIdle.Minimum = 5; _nudIdle.Maximum = 600;
        rows.Controls.Add(NumberRow(_nudIdle, "Забывать набранное", "сек",
            "После стольких секунд без ввода набранное перестаёт быть\nкандидатом на исправление."));
        return card;
    }

    private Control Footer()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4, Theme.S2, Theme.S4, Theme.S3),
        };

        var save = new FlatButton { Text = "Сохранить", Primary = true, Size = new Size(124, 34), DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = "Отмена", Size = new Size(104, 34), DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => ApplyToSettings();

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Canvas,
        };
        save.Margin = new Padding(Theme.S2, Theme.S1, 0, 0);
        cancel.Margin = new Padding(Theme.S2, Theme.S1, 0, 0);
        flow.Controls.Add(save);
        flow.Controls.Add(cancel);
        bar.Controls.Add(flow);

        AcceptButton = save;
        CancelButton = cancel;
        return bar;
    }

    // ------------------------------------------------------------- building blocks

    private static FlowLayoutPanel StackInside(Card card)
    {
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        card.Controls.Add(stack);
        return stack;
    }

    private static Control Divider() => new Panel
    {
        Width = CardWidth - Theme.S3 * 2,
        Height = 1,
        BackColor = Theme.Border,
        Margin = new Padding(0, Theme.S2, 0, Theme.S2),
    };

    private const int RowWidth = CardWidth - Theme.S3 * 2;

    /// <summary>Строка «заголовок + пояснение» слева и произвольный контрол справа.</summary>
    private static Panel Row(string title, string hint, Control right, int rightWidth, int height)
    {
        var row = new Panel
        {
            Width = RowWidth,
            Height = height,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };

        var lblTitle = new Label
        {
            Text = title,
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(0, 0),
            BackColor = Color.Transparent,
        };
        var lblHint = new Label
        {
            Text = hint,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(RowWidth - rightWidth - Theme.S3, 0),
            Location = new Point(0, 18),
            BackColor = Color.Transparent,
        };

        right.Width = rightWidth;
        right.Location = new Point(RowWidth - rightWidth, 0);
        right.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        row.Controls.Add(lblTitle);
        row.Controls.Add(lblHint);
        row.Controls.Add(right);
        return row;
    }

    private static Panel CheckRow(CheckBox box, string title, string hint)
    {
        box.Text = string.Empty;
        box.AutoSize = false;
        box.Size = new Size(20, 20);
        box.BackColor = Color.Transparent;
        box.Cursor = Cursors.Hand;
        return Row(title, hint, box, 20, 40);
    }

    private Panel HotkeyRow(string title, string hint, HotkeyDisplay display, Action record)
    {
        var right = new Panel { Height = 34, BackColor = Color.Transparent };

        display.Size = new Size(148, 30);
        display.Location = new Point(0, 2);

        var btn = new FlatButton { Text = "Изменить", Size = new Size(88, 30), Location = new Point(152, 2) };
        btn.Click += (_, _) => record();

        right.Controls.Add(display);
        right.Controls.Add(btn);
        return Row(title, hint, right, 240, 52);
    }

    private static Panel NumberRow(NumericUpDown nud, string title, string unit, string hint)
    {
        var right = new Panel { Height = 30, BackColor = Color.Transparent };

        nud.Size = new Size(64, 26);
        nud.Location = new Point(0, 2);
        nud.Font = Theme.Body;
        nud.BorderStyle = BorderStyle.FixedSingle;
        nud.TextAlign = HorizontalAlignment.Center;

        var lblUnit = new Label
        {
            Text = unit,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(70, 8),
            BackColor = Color.Transparent,
        };

        right.Controls.Add(nud);
        right.Controls.Add(lblUnit);
        return Row(title, hint, right, 104, 52);
    }

    // ------------------------------------------------------------------ data

    private void Populate()
    {
        _cbEnabled.Checked = _settings.Enabled;
        _cbAutostart.Checked = _settings.Autostart;
        _nudIdle.Value = Math.Clamp(_settings.BufferIdleTimeoutSeconds, (int)_nudIdle.Minimum, (int)_nudIdle.Maximum);
        _nudExpand.Value = Math.Clamp(_settings.ExpandWindowSeconds, (int)_nudExpand.Minimum, (int)_nudExpand.Maximum);
        _convertKeys.SetCombo(_convertHotkey.ToString());
        _caseKeys.SetCombo(_caseHotkey.ToString());
    }

    private void RecordInto(ref HotkeyConfig target, HotkeyDisplay display)
    {
        using var dlg = new HotkeyRecordDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
        {
            target = dlg.Result;
            display.SetCombo(target.ToString());
        }
    }

    private static HotkeyConfig Clone(HotkeyConfig h) => new()
    {
        Ctrl = h.Ctrl, Shift = h.Shift, Alt = h.Alt, Win = h.Win, Key = h.Key,
    };

    private void ApplyToSettings()
    {
        _settings.Enabled = _cbEnabled.Checked;
        _settings.Autostart = _cbAutostart.Checked;
        _settings.BufferIdleTimeoutSeconds = (int)_nudIdle.Value;
        _settings.ExpandWindowSeconds = (int)_nudExpand.Value;
        _settings.ConvertHotkey = _convertHotkey;
        _settings.ChangeCaseHotkey = _caseHotkey;
    }
}

/// <summary>
/// Диалог записи хоткея: копит нажатые клавиши и фиксирует комбинацию,
/// когда пользователь отпустил всё. Win-клавишу WinForms не отдаёт через
/// Modifiers, поэтому читаем её через GetAsyncKeyState.
/// </summary>
public sealed class HotkeyRecordDialog : Form
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12;

    public HotkeyConfig? Result { get; private set; }

    private readonly HotkeyDisplay _preview = new();
    private readonly Label _hint = new();
    private bool _ctrl, _shift, _alt, _win;
    private int _key;
    private bool _anyPressed;

    public HotkeyRecordDialog()
    {
        Text = "Новое сочетание";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 168);
        BackColor = Theme.Canvas;
        KeyPreview = true;

        var card = new Card
        {
            Location = new Point(Theme.S4, Theme.S4),
            Size = new Size(380 - Theme.S4 * 2, 72),
        };
        _preview.Size = new Size(card.Width - Theme.S3 * 2, 34);
        _preview.Location = new Point(Theme.S3, 19);
        _preview.SetCombo(string.Empty);
        card.Controls.Add(_preview);

        _hint.Text = "Нажмите сочетание и отпустите клавиши.\nAlt+Shift занят системой — выберите другое.";
        _hint.Font = Theme.Caption;
        _hint.ForeColor = Theme.TextMuted;
        _hint.AutoSize = true;
        _hint.BackColor = Color.Transparent;
        _hint.Location = new Point(Theme.S4, Theme.S4 + 72 + Theme.S3);

        Controls.Add(card);
        Controls.Add(_hint);

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
        if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0) _win = true;
        if (e.Control) _ctrl = true;
        if (e.Shift) _shift = true;
        if (e.Alt) _alt = true;
        if (!IsModifier(e.KeyCode)) _key = (int)e.KeyCode;

        _preview.SetCombo(Current().ToString());
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_anyPressed) return;

        bool stillDown =
            (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        if (stillDown) return;
        if (!_ctrl && !_shift && !_alt && !_win && _key == 0) return;

        // Alt+Shift — системный шорткат смены раскладки Windows: до нас он не дойдёт.
        if (_alt && _shift && !_ctrl && !_win)
        {
            _hint.Text = "Alt+Shift занят Windows (смена раскладки).\nВыберите другое сочетание.";
            _hint.ForeColor = Theme.AccentPressed;
            _ctrl = _shift = _alt = _win = false;
            _key = 0;
            _anyPressed = false;
            _preview.SetCombo(string.Empty);
            return;
        }

        Result = Current();
        DialogResult = DialogResult.OK;
        Close();
    }

    private HotkeyConfig Current() =>
        new() { Ctrl = _ctrl, Shift = _shift, Alt = _alt, Win = _win, Key = _key };
}
