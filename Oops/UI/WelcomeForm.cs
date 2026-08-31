using System.Drawing;
using System.Windows.Forms;
using Oops.Core;
using Oops.Settings;

namespace Oops.UI;

/// <summary>
/// Мастер первого запуска.
///
/// Нужен потому, что программа с виду ничего не делает: иконка в трее и всё.
/// Без объяснения модели «расширяющейся области» человек жмёт хоткей на всём
/// абзаце, получает исправленное последнее слово и решает, что оно сломано.
/// Второй смысл — явно показать и дать переназначить сочетания: сочетание,
/// которое ничего не делает, невозможно отличить от неработающей программы.
///
/// Разметка — та же дизайн-система, что и в SettingsForm: вложенные AutoSize
/// TableLayoutPanel, без фиксированных высот (ломаются при DPI 125/150%).
/// </summary>
public sealed class WelcomeForm : ThemedForm
{
    // Единственный источник ширины — колонка контента; всё внутри растягивается
    // якорями. Логика та же, что в SettingsForm — см. комментарий там.
    private const int ContentWidth = 580;

    /// <summary>
    /// Ширина поля с клавишами: «Ctrl+Shift+Win» помещается с запасом. Шире не
    /// надо — лишняя ширина отбирает место у подписи слева и заставляет её
    /// переноситься.
    /// </summary>
    private const int HotkeyWidth = 220;

    private readonly TableLayoutPanel _root;
    private readonly CheckBox _cbAutostart = new ToggleBox();
    private readonly HotkeyDisplay _convertKeys = new() { Interactive = true };
    private readonly HotkeyDisplay _caseKeys = new() { Interactive = true };

    private HotkeyConfig _convertHotkey;
    private HotkeyConfig _caseHotkey;

    public HotkeyConfig ConvertHotkey => _convertHotkey;
    public HotkeyConfig ChangeCaseHotkey => _caseHotkey;
    public bool AutostartWanted => _cbAutostart.Checked;

    public WelcomeForm(AppSettings settings)
    {
        _convertHotkey = Clone(settings.ConvertHotkey);
        _caseHotkey = Clone(settings.ChangeCaseHotkey);

        Text = L10n.T("welcome.window.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        // Фон, шрифт, DPI и тёмный заголовок окна приходят из ThemedForm.

        _root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            // Снизу S3, а не S2: вместе с отступом футера получается 24 —
            // столько же, сколько сверху и по бокам. Раньше низ был тоньше.
            Padding = new Padding(Theme.S4, Theme.S4, Theme.S4, Theme.S3),
            Margin = new Padding(0),
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));
        Controls.Add(_root);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // Галочка автозапуска отражает реестр: инсталлятор мог уже её поставить.
        _cbAutostart.Checked = Autostart.IsEnabled();

        // Клик по самим клавишам открывает запись — подписка одна на всё окно.
        _convertKeys.Click += (_, _) => Record(ref _convertHotkey, _convertKeys);
        _caseKeys.Click += (_, _) => Record(ref _caseHotkey, _caseKeys);

        ShowPage(0);
    }

    /// <summary>
    /// Показывает мастер, если он ещё не показывался, и применяет выбор.
    /// Хоткеи на это время выключены: иначе нажатие текущего сочетания в диалоге
    /// записи перехватил бы хук и записалось бы не то, что нажали.
    /// </summary>
    public static void ShowIfFirstRun(App app)
    {
        if (app.Settings.FirstRunCompleted) return;

        app.HotkeysSuspended = true;
        try
        {
            using var wizard = new WelcomeForm(app.Settings);
            wizard.ShowDialog();

            app.Settings.ConvertHotkey = wizard.ConvertHotkey;
            app.Settings.ChangeCaseHotkey = wizard.ChangeCaseHotkey;
            Autostart.Set(wizard.AutostartWanted);

            // Ставим отметку в любом случае, даже если окно просто закрыли крестиком:
            // мастер не должен встречать человека при каждом запуске.
            app.Settings.FirstRunCompleted = true;
            app.Settings.Save();
            app.ApplySettings();
        }
        finally
        {
            app.HotkeysSuspended = false;
        }
    }

    // ---------------------------------------------------------------- страницы

    private void ShowPage(int page)
    {
        SuspendLayout();
        _root.SuspendLayout();
        _root.Controls.Clear();
        _root.RowStyles.Clear();
        _root.RowCount = 0;

        if (page == 0) BuildIntro();
        else BuildHotkeys();

        _root.ResumeLayout(true);
        ResumeLayout(true);
    }

    private void BuildIntro()
    {
        AddRow(_root, Heading("oops",
            L10n.T("welcome.intro.subtitle")));

        AddRow(_root, SectionLabel(L10n.T("welcome.section.how")));

        var card = NewCard(out var rows);
        AddRow(rows, Paragraph(
            L10n.T("welcome.how.p1")));
        AddRow(rows, Example(
            L10n.T("welcome.example.typed"),
            L10n.T("welcome.example.first"),
            L10n.T("welcome.example.second")));
        AddRow(rows, Paragraph(
            L10n.T("welcome.how.p2")));
        AddRow(rows, Divider());
        AddRow(rows, Paragraph(
            L10n.T("welcome.how.selection")));
        AddRow(rows, Divider());
        AddRow(rows, Paragraph(
            L10n.T("welcome.how.clipboard")));
        AddRow(_root, card);

        AddRow(_root, Footer(L10n.T("common.next"), () => ShowPage(1), showBack: false));
    }

    private void BuildHotkeys()
    {
        AddRow(_root, Heading(L10n.T("welcome.hotkeys.title"),
            L10n.T("welcome.hotkeys.subtitle")));

        var keys = NewCard(out var keyRows);
        AddRow(keyRows, HotkeyRow(L10n.T("hotkey.layout"), L10n.T("hotkey.layout.hint"),
            _convertKeys, () => Record(ref _convertHotkey, _convertKeys)));
        AddRow(keyRows, Divider());
        AddRow(keyRows, HotkeyRow(L10n.T("hotkey.case"), L10n.T("hotkey.case.hint"),
            _caseKeys, () => Record(ref _caseHotkey, _caseKeys)));
        _convertKeys.SetCombo(_convertHotkey.ToString());
        _caseKeys.SetCombo(_caseHotkey.ToString());
        AddRow(_root, keys);

        AddRow(_root, Note(
            L10n.T("welcome.note.altShift")));

        AddRow(_root, SectionLabel(L10n.T("welcome.section.startup")));
        var start = NewCard(out var startRows);
        AddRow(startRows, CheckRow(_cbAutostart, L10n.T("settings.autostart"),
            L10n.T("settings.autostart.hint")));
        AddRow(_root, start);

        AddRow(_root, Note(
            L10n.T("welcome.note.tray")));

        AddRow(_root, Footer(L10n.T("common.done"), Finish, showBack: true));
    }

    private void Finish()
    {
        if (_convertHotkey.SameCombo(_caseHotkey))
        {
            Notice.Warn(this, L10n.T("hotkey.clash.title"),
                L10n.T("hotkey.clash.body"), L10n.T("hotkey.clash.hint"));
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Record(ref HotkeyConfig target, HotkeyDisplay display)
    {
        using var dlg = new HotkeyRecordDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
        {
            target = dlg.Result;
            display.SetCombo(target.ToString());
        }
    }

    // ------------------------------------------------------------ строительные блоки

    private static void AddRow(TableLayoutPanel host, Control child)
    {
        // Ребёнок с дефолтным якорем растягивается на ширину колонки —
        // ширину диктует колонка, а не контрол. Явные якоря не трогаем.
        if (child.Anchor == (AnchorStyles.Top | AnchorStyles.Left))
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }

    private static TableLayoutPanel Stack()
    {
        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return stack;
    }

    private static Card NewCard(out TableLayoutPanel rows)
    {
        // Высота — не AutoSize: он меряет содержимое до переноса строк и резал
        // низ карточки. Берём фактическую высоту рядов после раскладки.
        var card = new Card { Margin = new Padding(0) };
        var r = Stack();
        r.Dock = DockStyle.Top;   // уважает Padding карточки с обеих сторон
        card.Controls.Add(r);
        r.SizeChanged += (_, _) => card.Height = r.Height + card.Padding.Vertical;
        rows = r;
        return card;
    }

    private static Control Heading(string title, string subtitle)
    {
        var stack = Stack();
        stack.Margin = new Padding(0, 0, 0, Theme.S2);
        AddRow(stack, new Label
        {
            Text = title,
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });
        AddRow(stack, new Label
        {
            Text = subtitle,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
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

    /// <summary>Абзац внутри карточки: переносится по ширине колонки.</summary>
    private static Control Paragraph(string text) => new Label
    {
        Text = text,
        Font = Theme.Body,
        ForeColor = Theme.Text,
        AutoSize = true,
        Margin = new Padding(0),
        BackColor = Color.Transparent,
    };

    private static Control Note(string text) => new Label
    {
        Text = text,
        Font = Theme.Caption,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        Margin = new Padding(Theme.S1, Theme.S2, Theme.S1, 0),
        BackColor = Color.Transparent,
    };

    /// <summary>Пример «до/после» моноширинным шрифтом на подложке.</summary>
    private static Control Example(params string[] lines) => new Label
    {
        Text = string.Join(Environment.NewLine, lines),
        Font = Theme.Mono,          // шрифт из дизайн-системы, а не свой на месте
        ForeColor = Theme.Text,
        BackColor = Theme.KeyCapFill,
        AutoSize = true,
        Padding = new Padding(Theme.S2),
        Margin = new Padding(0, Theme.S2, 0, Theme.S2),
    };

    private static Control Divider() => new Panel
    {
        Height = 1,
        BackColor = Theme.Border,
        Margin = new Padding(0, Theme.S2, 0, Theme.S2),
    };

    /// <summary>
    /// Строка «заголовок + пояснение» слева, контрол справа.
    /// <paramref name="onActivate"/> — клик по подписи делает то же, что и контрол:
    /// галочка 20×20 меньше, чем человек целится мышью.
    /// </summary>
    private static TableLayoutPanel Row(string title, string hint, Control right,
        Action? onActivate = null)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, Theme.MinHitHeight),
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var text = Stack();
        var titleLabel = new Label
        {
            Text = title,
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
            BackColor = Color.Transparent,
        };
        var hintLabel = new Label
        {
            Text = hint,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };
        AddRow(text, titleLabel);
        AddRow(text, hintLabel);
        text.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        if (onActivate != null)
        {
            foreach (var c in new Control[] { titleLabel, hintLabel })
            {
                c.Cursor = Cursors.Hand;
                c.Click += (_, _) => onActivate();
            }
        }

        right.Anchor = AnchorStyles.Right;
        right.Margin = new Padding(Theme.S3, 0, 0, 0);

        row.Controls.Add(text, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    private static Control CheckRow(CheckBox box, string title, string hint)
    {
        // ToggleBox рисует себя сам во весь свой прямоугольник.
        box.Text = string.Empty;
        box.AutoSize = false;
        box.Size = new Size(20, 20);
        return Row(title, hint, box, () => box.Checked = !box.Checked);
    }

    private static Control HotkeyRow(string title, string hint, HotkeyDisplay display, Action record)
    {
        display.Size = new Size(HotkeyWidth, 30);
        display.Margin = new Padding(0, 0, Theme.S2, 0);
        // Click у display подписан один раз в конструкторе: страница пересобирается
        // при каждом «Назад/Далее», и подписка здесь копилась бы с каждым разом.

        var btn = new FlatButton
        {
            Text = L10n.T("hotkey.change"),
            AutoSize = true,
            MinimumSize = new Size(92, 30),
            Margin = new Padding(0),
        };
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

        return Row(title, hint, group);
    }

    private Control Footer(string primaryText, Action primaryAction, bool showBack)
    {
        var primary = new FlatButton
        {
            Text = primaryText,
            Primary = true,
            AutoSize = true,
            MinimumSize = new Size(124, 34),
            Margin = new Padding(Theme.S2, 0, 0, 0),
        };
        primary.Click += (_, _) => primaryAction();

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Margin = new Padding(0),
        };
        flow.Controls.Add(primary);

        if (showBack)
        {
            var back = new FlatButton
            {
                Text = L10n.T("common.back"),
                AutoSize = true,
                MinimumSize = new Size(104, 34),
                Margin = new Padding(Theme.S2, 0, 0, 0),
            };
            back.Click += (_, _) => ShowPage(0);
            flow.Controls.Add(back);
        }

        AcceptButton = primary;

        // Подвал во всю ширину — та же причина, что в SettingsForm: Anchor = Right
        // позиционирует панель, но не заставляет родителя быть нужной ширины.
        var bar = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, Theme.S4, 0, Theme.S2),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bar.Controls.Add(new Panel { Width = 0, Height = 0, Margin = new Padding(0) }, 0, 0);
        bar.Controls.Add(flow, 1, 0);
        return bar;
    }

    private static HotkeyConfig Clone(HotkeyConfig h) => new()
    {
        Ctrl = h.Ctrl, Shift = h.Shift, Alt = h.Alt, Win = h.Win, Key = h.Key,
    };
}
