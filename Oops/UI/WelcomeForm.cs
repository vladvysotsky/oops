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
    private const int ContentWidth = 580;
    private const int CardInnerWidth = ContentWidth - Theme.S3 * 2;
    /// <summary>
    /// Ширина поля с клавишами. С запасом на три клавиши с длинными именами
    /// («Ctrl+Shift+Win»): раньше 190px не хватало, и третья обрезалась.
    /// Расширено вместе с ContentWidth на одну и ту же величину, чтобы ширина
    /// подписей слева не изменилась.
    /// </summary>
    private const int HotkeyWidth = 250;
    private const int ReservedHotkey = HotkeyWidth + Theme.S2 + 92;  // + отступ + кнопка

    private readonly TableLayoutPanel _root;
    private readonly CheckBox _cbAutostart = new();
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

        Text = "Добро пожаловать в oops";
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
            Padding = new Padding(Theme.S4, Theme.S4, Theme.S4, Theme.S2),
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
            "Правит раскладку и регистр текста, который вы только что набрали. "
            + "Работает во всех приложениях Windows."));

        AddRow(_root, SectionLabel("КАК ЭТО РАБОТАЕТ"));

        var card = NewCard(out var rows);
        AddRow(rows, Paragraph(
            "Программа не угадывает, где вы сбились с раскладки — границу задаёте вы "
            + "количеством нажатий хоткея:"));
        AddRow(rows, Example(
            "набрали:   ghjdthrf njuj rfr 'nj hf,jnftn",
            "1-е нажатие:  ghjdthrf njuj rfr 'nj работает",
            "2-е нажатие:  проверка того как это работает"));
        AddRow(rows, Paragraph(
            "Первое нажатие правит последнее слово, второе — весь набранный текст. "
            + "Нажатия должны идти подряд, в пределах пары секунд."));
        AddRow(rows, Divider());
        AddRow(rows, Paragraph(
            "Если текст выделен мышью, хоткей преобразует всё выделение целиком."));
        AddRow(rows, Divider());
        AddRow(rows, Paragraph(
            "Исправленный текст печатается эмуляцией клавиатуры и в буфер обмена "
            + "не попадает — история Win+V остаётся чистой."));
        AddRow(_root, card);

        AddRow(_root, Footer("Далее", () => ShowPage(1), showBack: false));
    }

    private void BuildHotkeys()
    {
        AddRow(_root, Heading("Горячие клавиши",
            "Сочетание может быть из одних модификаторов (Ctrl + Win), а может "
            + "включать обычную клавишу — например Ctrl + Alt + X. Всё меняется "
            + "потом в настройках."));

        var keys = NewCard(out var keyRows);
        AddRow(keyRows, HotkeyRow("Раскладка", "Меняет RU ↔ EN",
            _convertKeys, () => Record(ref _convertHotkey, _convertKeys)));
        AddRow(keyRows, Divider());
        AddRow(keyRows, HotkeyRow("Регистр", "ВЕРХНИЙ ↔ нижний",
            _caseKeys, () => Record(ref _caseHotkey, _caseKeys)));
        _convertKeys.SetCombo(_convertHotkey.ToString());
        _caseKeys.SetCombo(_caseHotkey.ToString());
        AddRow(_root, keys);

        AddRow(_root, Note(
            "Alt + Shift назначить нельзя: эту комбинацию Windows забирает себе "
            + "под смену раскладки, до приложения она не доходит."));

        AddRow(_root, SectionLabel("ЗАПУСК"));
        var start = NewCard(out var startRows);
        AddRow(startRows, CheckRow(_cbAutostart, "Запускать при входе в Windows",
            "Иначе после перезагрузки придётся открывать вручную"));
        AddRow(_root, start);

        AddRow(_root, Note(
            "Программа живёт в трее. Двойной клик по иконке открывает настройки."));

        AddRow(_root, Footer("Готово", Finish, showBack: true));
    }

    private void Finish()
    {
        if (_convertHotkey.SameCombo(_caseHotkey))
        {
            Notice.Warn(this, "Сочетания совпадают",
                "Раскладка и регистр не могут висеть на одном сочетании: сработает "
                + "только первое, второе будет молчать без единого признака.",
                "Назначьте разные — например, раскладке Ctrl + Win, регистру Alt + Win.");
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
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }

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
        rows.Width = CardInnerWidth;
        card.Controls.Add(rows);
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
            MaximumSize = new Size(ContentWidth, 0),
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

    /// <summary>Абзац внутри карточки. MaximumSize обязателен — иначе AutoSize-лейбл
    /// требует полную ширину строки и растягивает карточку за границу окна.</summary>
    private static Control Paragraph(string text) => new Label
    {
        Text = text,
        Font = Theme.Body,
        ForeColor = Theme.Text,
        AutoSize = true,
        MaximumSize = new Size(CardInnerWidth, 0),
        Margin = new Padding(0),
        BackColor = Color.Transparent,
    };

    private static Control Note(string text) => new Label
    {
        Text = text,
        Font = Theme.Caption,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        MaximumSize = new Size(ContentWidth - Theme.S1 * 2, 0),
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
        MaximumSize = new Size(CardInnerWidth, 0),
        Padding = new Padding(Theme.S2),
        Margin = new Padding(0, Theme.S2, 0, Theme.S2),
    };

    private static Control Divider() => new Panel
    {
        Height = 1,
        Width = CardInnerWidth,
        BackColor = Theme.Border,
        Margin = new Padding(0, Theme.S2, 0, Theme.S2),
    };

    /// <summary>
    /// Строка «заголовок + пояснение» слева, контрол справа.
    /// <paramref name="onActivate"/> — клик по подписи делает то же, что и контрол:
    /// галочка 20×20 меньше, чем человек целится мышью.
    /// </summary>
    private static TableLayoutPanel Row(string title, string hint, Control right, int reservedRight,
        Action? onActivate = null)
    {
        int textWidth = CardInnerWidth - reservedRight - Theme.S3;

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, Theme.MinHitHeight),
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Width = CardInnerWidth,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, textWidth + Theme.S3));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, reservedRight));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var text = Stack();
        text.Anchor = AnchorStyles.Left;
        var titleLabel = new Label
        {
            Text = title,
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            Margin = new Padding(0, 0, 0, 2),
            BackColor = Color.Transparent,
        };
        var hintLabel = new Label
        {
            Text = hint,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };
        AddRow(text, titleLabel);
        AddRow(text, hintLabel);

        if (onActivate != null)
        {
            foreach (var c in new Control[] { titleLabel, hintLabel })
            {
                c.Cursor = Cursors.Hand;
                c.Click += (_, _) => onActivate();
            }
        }

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
        box.ForeColor = Theme.Text;
        box.Cursor = Cursors.Hand;
        return Row(title, hint, box, 24, () => box.Checked = !box.Checked);
    }

    private static Control HotkeyRow(string title, string hint, HotkeyDisplay display, Action record)
    {
        display.Size = new Size(HotkeyWidth, 30);
        display.Margin = new Padding(0, 0, Theme.S2, 0);
        // Click у display подписан один раз в конструкторе: страница пересобирается
        // при каждом «Назад/Далее», и подписка здесь копилась бы с каждым разом.

        var btn = new FlatButton
        {
            Text = "Изменить",
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

        return Row(title, hint, group, ReservedHotkey);
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
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, Theme.S4, 0, Theme.S2),
        };
        flow.Controls.Add(primary);

        if (showBack)
        {
            var back = new FlatButton
            {
                Text = "Назад",
                AutoSize = true,
                MinimumSize = new Size(104, 34),
                Margin = new Padding(Theme.S2, 0, 0, 0),
            };
            back.Click += (_, _) => ShowPage(0);
            flow.Controls.Add(back);
        }

        AcceptButton = primary;
        return flow;
    }

    private static HotkeyConfig Clone(HotkeyConfig h) => new()
    {
        Ctrl = h.Ctrl, Shift = h.Shift, Alt = h.Alt, Win = h.Win, Key = h.Key,
    };
}
