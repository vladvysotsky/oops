using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyLangSwitcher.Core;
using KeyLangSwitcher.Settings;

namespace KeyLangSwitcher.UI;

/// <summary>
/// Окно настроек по дизайн-системе из <see cref="Theme"/>.
///
/// Вся разметка построена на вложенных TableLayoutPanel с AutoSize. Фиксированные
/// высоты строк и карточек намеренно не используются: при DPI-масштабировании
/// (125%/150%) и переносе строк контент в них не помещается и обрезается.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private readonly CheckBox _cbEnabled = new();
    private readonly CheckBox _cbAutostart = new();
    private readonly CheckBox _cbAutoUpdate = new();
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

        BuildLayout();
        Populate();

        // Подгоняем окно под фактическую высоту содержимого, чтобы не появлялась прокрутка.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    // ---------------------------------------------------------------- layout

    /// <summary>Ширина колонки контента (карточки, заголовки). Масштабируется системой по DPI.</summary>
    private const int ContentWidth = 520;

    /// <summary>Ширина внутренностей карточки (за вычетом её padding).</summary>
    private const int CardInnerWidth = ContentWidth - Theme.S3 * 2;

    // Сколько места резервирует правый контрол в строке. Нужно, чтобы ограничить
    // ширину подписей: без ограничения AutoSize-лейбл требует свою полную ширину
    // и выдавливает правую колонку за границу карточки.
    private const int ReservedCheck = 24;
    private const int ReservedHotkey = 250;   // клавиши 150 + отступ 8 + кнопка 92
    private const int ReservedNumber = 104;   // поле 64 + отступ 8 + подпись

    private void BuildLayout()
    {
        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4, Theme.S4, Theme.S4, Theme.S2),
            Margin = new Padding(0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

        AddAutoRow(content, Header());
        AddAutoRow(content, SectionLabel("ОБЩИЕ"));
        AddAutoRow(content, GeneralCard());
        AddAutoRow(content, SectionLabel("ГОРЯЧИЕ КЛАВИШИ"));
        AddAutoRow(content, HotkeysCard());
        AddAutoRow(content, SectionLabel("ПОВЕДЕНИЕ"));
        AddAutoRow(content, BehaviourCard());
        AddAutoRow(content, Footer());

        Controls.Add(content);
    }

    private static void AddAutoRow(TableLayoutPanel host, Control child)
    {
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }

    private Control Header()
    {
        var stack = Stack();
        stack.Margin = new Padding(0, 0, 0, Theme.S2);

        AddAutoRow(stack, new Label
        {
            Text = "KeyLangSwitcher",
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });
        AddAutoRow(stack, new Label
        {
            Text = "Правит раскладку и регистр набранного текста. Границу задаёте вы: "
                 + "каждое следующее нажатие хоткея захватывает ещё одно слово.",
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),  // перенос по ширине колонки
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        });
        return stack;
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

    private Control GeneralCard()
    {
        var card = NewCard(out var rows);
        AddAutoRow(rows, CheckRow(_cbEnabled, "Включено",
            "Глобально включает и выключает горячие клавиши"));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, CheckRow(_cbAutostart, "Запускать при старте Windows",
            "Запись в реестр HKCU\\…\\Run"));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, CheckRow(_cbAutoUpdate, "Проверять обновления",
            "Раз в сутки, через релизы на GitHub"));
        return card;
    }

    private Control HotkeysCard()
    {
        var card = NewCard(out var rows);
        AddAutoRow(rows, HotkeyRow(
            "Раскладка", "Меняет RU ↔ EN у последнего слова",
            _convertKeys, () => RecordInto(ref _convertHotkey, _convertKeys)));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, HotkeyRow(
            "Регистр", "ВЕРХНИЙ ↔ нижний по той же логике",
            _caseKeys, () => RecordInto(ref _caseHotkey, _caseKeys)));
        return card;
    }

    private Control BehaviourCard()
    {
        var card = NewCard(out var rows);

        _nudExpand.Minimum = 1; _nudExpand.Maximum = 10;
        AddAutoRow(rows, NumberRow(_nudExpand, "Окно расширения", "сек",
            "Столько времени нажатие расширяет ту же область"));
        AddAutoRow(rows, Divider());

        _nudIdle.Minimum = 5; _nudIdle.Maximum = 600;
        AddAutoRow(rows, NumberRow(_nudIdle, "Забывать набранное", "сек",
            "Через столько секунд без ввода буфер очищается"));
        return card;
    }

    private Control Footer()
    {
        var save = new FlatButton { Text = "Сохранить", Primary = true, Size = new Size(124, 34), DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = "Отмена", Size = new Size(104, 34), DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => ApplyToSettings();

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, Theme.S4, 0, Theme.S2),
        };
        save.Margin = new Padding(Theme.S2, 0, 0, 0);
        cancel.Margin = new Padding(Theme.S2, 0, 0, 0);
        flow.Controls.Add(save);
        flow.Controls.Add(cancel);

        AcceptButton = save;
        CancelButton = cancel;
        return flow;
    }

    // ------------------------------------------------------------- building blocks

    /// <summary>Вертикальный стек с авторазмером — базовый строительный блок разметки.</summary>
    private static TableLayoutPanel Stack() => new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
    };

    private static Card NewCard(out TableLayoutPanel rows)
    {
        var card = new Card
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Width = ContentWidth,
        };
        rows = Stack();
        rows.Width = ContentWidth - Theme.S3 * 2;
        card.Controls.Add(rows);
        return card;
    }

    private static Control Divider() => new Panel
    {
        Height = 1,
        Width = ContentWidth - Theme.S3 * 2,
        BackColor = Theme.Border,
        Margin = new Padding(0, Theme.S2, 0, Theme.S2),
    };

    /// <summary>
    /// Строка «заголовок + пояснение» слева, контрол справа.
    /// <paramref name="reservedRight"/> — сколько места занимает правый контрол;
    /// на эту величину сужается допустимая ширина подписей. Без такого лимита
    /// AutoSize-лейбл требует свою полную ширину и выталкивает контрол за границу
    /// карточки (текст не переносится, а строка становится шире карточки).
    /// </summary>
    private static TableLayoutPanel Row(string title, string hint, Control right, int reservedRight)
    {
        int textWidth = CardInnerWidth - reservedRight - Theme.S3;

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Width = CardInnerWidth,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, textWidth + Theme.S3));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, reservedRight));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var text = Stack();
        text.Anchor = AnchorStyles.Left;
        AddAutoRow(text, new Label
        {
            Text = title,
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            Margin = new Padding(0, 0, 0, 2),
            BackColor = Color.Transparent,
        });
        AddAutoRow(text, new Label
        {
            Text = hint,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        });

        right.Anchor = AnchorStyles.Right;
        right.Margin = new Padding(0);

        row.Controls.Add(text, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    private static Control CheckRow(CheckBox box, string title, string hint)
    {
        box.Text = string.Empty;
        box.AutoSize = false;
        box.Size = new Size(20, 20);
        box.BackColor = Color.Transparent;
        box.Cursor = Cursors.Hand;
        return Row(title, hint, box, ReservedCheck);
    }

    private static Control HotkeyRow(string title, string hint, HotkeyDisplay display, Action record)
    {
        display.Size = new Size(150, 30);
        display.Margin = new Padding(0, 0, Theme.S2, 0);

        var btn = new FlatButton { Text = "Изменить", Size = new Size(92, 30), Margin = new Padding(0) };
        btn.Click += (_, _) => record();

        var group = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        group.Controls.Add(display);
        group.Controls.Add(btn);

        return Row(title, hint, group, ReservedHotkey);
    }

    private static Control NumberRow(NumericUpDown nud, string title, string unit, string hint)
    {
        nud.Size = new Size(64, 26);
        nud.Font = Theme.Body;
        nud.BorderStyle = BorderStyle.FixedSingle;
        nud.TextAlign = HorizontalAlignment.Center;
        nud.Margin = new Padding(0, 2, Theme.S2, 0);

        var group = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        group.Controls.Add(nud);
        group.Controls.Add(new Label
        {
            Text = unit,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent,
        });

        return Row(title, hint, group, ReservedNumber);
    }

    // ------------------------------------------------------------------ data

    private void Populate()
    {
        _cbEnabled.Checked = _settings.Enabled;
        _cbAutostart.Checked = _settings.Autostart;
        _cbAutoUpdate.Checked = _settings.AutoCheckUpdates;
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
        _settings.AutoCheckUpdates = _cbAutoUpdate.Checked;
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
        BackColor = Theme.Canvas;
        Font = Theme.Body;
        KeyPreview = true;

        var card = new Card
        {
            Width = 340,
            Height = 66,
            Margin = new Padding(0, 0, 0, Theme.S3),
        };
        _preview.Size = new Size(340 - Theme.S3 * 2, 34);
        _preview.Location = new Point(Theme.S3, 16);
        _preview.SetCombo(string.Empty);
        card.Controls.Add(_preview);

        _hint.Text = "Нажмите сочетание и отпустите клавиши.";
        _hint.Font = Theme.Caption;
        _hint.ForeColor = Theme.TextMuted;
        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(340, 0);
        _hint.Margin = new Padding(0);
        _hint.BackColor = Color.Transparent;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card, 0, 0);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_hint, 0, 1);
        root.RowCount = 2;

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

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
            _hint.Text = "Alt+Shift занят Windows (смена раскладки). Выберите другое.";
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
