using System.Drawing;
using System.Windows.Forms;
using Oops.Core;

namespace Oops.UI;

/// <summary>
/// Замена в выделенном тексте: обычная и по регулярному выражению.
///
/// Предпросмотр здесь не украшение. Регулярное выражение — единственное место
/// в программе, где человек может одним нажатием испортить весь выделенный
/// кусок и не понять, чем именно. Поэтому результат и число замен видны ДО
/// того, как что-то будет напечатано в чужое окно, а сама замена всегда
/// применяется к исходному тексту целиком — «применить дважды» невозможно.
///
/// Мастер — не построитель выражений с деревом узлов, а две простые вещи:
/// готовые правила под частые задачи и кирпичики, которые вставляются в поле
/// поиска. Синтаксис вспоминать не нужно, а понимать, что получилось, —
/// нужно, и для этого есть предпросмотр.
/// </summary>
internal sealed class ReplaceForm : ThemedForm
{
    private static readonly int ContentWidth = Theme.Px(620);
    private static readonly int CardInnerWidth = ContentWidth - Theme.S3 * 2;

    private readonly string _source;

    private readonly TextBox _find = new();
    private readonly TextBox _replace = new();
    private readonly CheckBox _regex = new ToggleBox();
    private readonly CheckBox _caseSensitive = new ToggleBox();
    private readonly CheckBox _wholeWord = new ToggleBox();
    private readonly Label _status = new();
    private readonly TextBox _preview = new();
    private readonly FlatButton _apply;

    private string _result;

    private ReplaceForm(string source)
    {
        _source = source;
        _result = source;

        Text = L10n.T("replace.window.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        _apply = new FlatButton
        {
            Text = L10n.T("replace.apply"),
            Primary = true,
            AutoSize = true,
            MinimumSize = new Size(Theme.Px(140), 0),
            DialogResult = DialogResult.OK,
        };
        var cancel = new FlatButton
        {
            Text = L10n.T("common.cancel"),
            AutoSize = true,
            MinimumSize = new Size(Theme.Px(104), 0),
            DialogResult = DialogResult.Cancel,
        };

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4),
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

        Add(root, Section(L10n.T("replace.section.what")));
        Add(root, FieldsCard());
        Add(root, Section(L10n.T("replace.section.wizard")));
        Add(root, WizardCard());
        Add(root, Section(L10n.T("replace.section.preview")));
        Add(root, PreviewCard());
        Add(root, ButtonBar.Create(ContentWidth, new Padding(0, Theme.S4, 0, 0), _apply, cancel));

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        CancelButton = cancel;
        ActiveControl = _find;

        Recalculate();
    }

    /// <summary>
    /// Показывает диалог. Возвращает новый текст или null, если человек
    /// передумал либо менять оказалось нечего.
    /// </summary>
    public static string? Ask(string selection)
    {
        using var form = new ReplaceForm(selection);
        if (form.ShowDialog() != DialogResult.OK) return null;
        return form._result == selection ? null : form._result;
    }

    // -------------------------------------------------------------- вёрстка

    private Control FieldsCard()
    {
        var card = NewCard(out var rows);

        Add(rows, FieldRow(L10n.T("replace.find"), _find));
        Add(rows, FieldRow(L10n.T("replace.replaceWith"), _replace));

        var flags = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, Theme.S2, 0, 0),
            Width = CardInnerWidth,
        };
        flags.Controls.Add(Flag(_regex, L10n.T("replace.flag.regex")));
        flags.Controls.Add(Flag(_caseSensitive, L10n.T("replace.flag.case")));
        flags.Controls.Add(Flag(_wholeWord, L10n.T("replace.flag.wholeWord")));
        Add(rows, flags);

        return card;
    }

    /// <summary>Галочка с подписью — обе кликабельны, обе в одну строку.</summary>
    private Control Flag(CheckBox box, string title)
    {
        box.Text = string.Empty;
        box.AutoSize = false;
        box.Size = new Size(Theme.Px(20), Theme.Px(20));
        box.Margin = new Padding(0, Theme.Px(3), Theme.S1, 0);
        box.BackColor = Color.Transparent;
        box.CheckedChanged += (_, _) => Recalculate();

        var label = new Label
        {
            Text = title,
            Font = Theme.Body,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, Theme.Px(4), Theme.S4, 0),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
        };
        label.Click += (_, _) => box.Checked = !box.Checked;

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        row.Controls.Add(box);
        row.Controls.Add(label);
        return row;
    }

    private Control FieldRow(string title, TextBox box)
    {
        box.Width = CardInnerWidth;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Theme.Surface;
        box.ForeColor = Theme.Text;
        box.Font = Theme.Mono;          // в выражении важен каждый символ
        box.Margin = new Padding(0, 0, 0, Theme.S2);
        box.TextChanged += (_, _) => Recalculate();

        var rows = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Width = CardInnerWidth,
        };
        Add(rows, new Label
        {
            Text = title,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });
        Add(rows, box);
        return rows;
    }

    private Control WizardCard()
    {
        var card = NewCard(out var rows);

        Add(rows, new Label
        {
            Text = L10n.T("replace.wizard.hint"),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(CardInnerWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S2),
            BackColor = Color.Transparent,
        });

        // Готовые правила заполняют оба поля разом: чаще всего человеку нужно
        // не «своё выражение», а одна из этих восьми вещей.
        var recipes = Chips();
        foreach (var recipe in TextReplacer.Recipes)
        {
            var r = recipe;
            recipes.Controls.Add(Chip(L10n.T(r.TitleKey), () =>
            {
                _find.Text = r.Rule.Find;
                _replace.Text = r.Rule.Replace;
                _regex.Checked = r.Rule.Regex;
                _caseSensitive.Checked = r.Rule.CaseSensitive;
                _wholeWord.Checked = r.Rule.WholeWord;
            }));
        }
        Add(rows, recipes);

        Add(rows, new Label
        {
            Text = L10n.T("replace.wizard.blocks"),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, Theme.S2, 0, Theme.S1),
            BackColor = Color.Transparent,
        });

        // Кирпичики вставляются в позицию курсора и включают режим регулярок
        // сами: вставить «\d+» и не понять, почему ищется буквальный текст, —
        // ровно та ловушка, ради которой мастер и делался.
        var blocks = Chips();
        foreach (var block in TextReplacer.Blocks)
        {
            var b = block;
            blocks.Controls.Add(Chip(L10n.T(b.TitleKey), () =>
            {
                _regex.Checked = true;
                int at = _find.SelectionStart;
                _find.Text = _find.Text.Insert(at, b.Pattern);
                _find.SelectionStart = at + b.Pattern.Length;
                _find.Focus();
            }));
        }
        Add(rows, blocks);

        return card;
    }

    private FlowLayoutPanel Chips() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
        Width = CardInnerWidth,
    };

    private static Control Chip(string text, Action onClick)
    {
        var button = new FlatButton
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 0, Theme.S1, Theme.S1),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private Control PreviewCard()
    {
        var card = NewCard(out var rows);

        _status.Font = Theme.Caption;
        _status.ForeColor = Theme.TextMuted;
        _status.AutoSize = true;
        _status.MaximumSize = new Size(CardInnerWidth, 0);
        _status.Margin = new Padding(0, 0, 0, Theme.S2);
        _status.BackColor = Color.Transparent;
        Add(rows, _status);

        _preview.Multiline = true;
        _preview.ReadOnly = true;
        _preview.ScrollBars = ScrollBars.Vertical;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.BackColor = Theme.Surface;
        _preview.ForeColor = Theme.TextMuted;
        _preview.Font = Theme.Mono;
        _preview.Width = CardInnerWidth;
        _preview.Height = Theme.Px(150);
        _preview.Margin = new Padding(0);
        _preview.TabStop = false;
        Add(rows, _preview);

        return card;
    }

    // --------------------------------------------------------------- логика

    private void Recalculate()
    {
        var rule = new ReplaceRule(_find.Text, _replace.Text,
            _regex.Checked, _caseSensitive.Checked, _wholeWord.Checked);

        // «Целые слова» и «регистр» к пустому поиску отношения не имеют, но
        // включать их можно всегда — путаницы это не создаёт.
        var result = TextReplacer.Apply(_source, rule);
        _result = result.Text;

        _preview.Text = result.Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        if (result.Failed)
        {
            _status.Text = L10n.T("replace.status.badRegex", result.Error!);
            _status.ForeColor = Theme.Danger;
            _apply.Enabled = false;
            return;
        }

        _apply.Enabled = result.Count > 0;
        if (_find.Text.Length == 0)
        {
            _status.Text = L10n.T("replace.status.empty");
            _status.ForeColor = Theme.TextMuted;
        }
        else if (result.Count == 0)
        {
            _status.Text = L10n.T("replace.status.none");
            _status.ForeColor = Theme.Warning;
        }
        else
        {
            _status.Text = L10n.T("replace.status.count", result.Count);
            _status.ForeColor = Theme.Accent;
        }
    }

    // ------------------------------------------------------------- мелочёвка

    private static Control Section(string text) => new Label
    {
        Text = text,
        Font = Theme.SectionLabel,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        Margin = new Padding(Theme.S1, Theme.S3, 0, Theme.S2),
        BackColor = Color.Transparent,
    };

    private static Card NewCard(out TableLayoutPanel rows)
    {
        // Ширина — сразу: до раскладки карточка думает, что она 200 px, и
        // абзацы внутри переносятся не там, где будут (см. CLAUDE.md).
        var card = new Card { Margin = new Padding(0), Width = ContentWidth };
        var r = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Dock = DockStyle.Top,
        };
        r.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        card.Controls.Add(r);
        r.SizeChanged += (_, _) => card.Height = r.Height + card.Padding.Vertical;
        card.Height = r.Height + card.Padding.Vertical;
        rows = r;
        return card;
    }

    private static void Add(TableLayoutPanel host, Control child)
    {
        if (child.Anchor == (AnchorStyles.Top | AnchorStyles.Left))
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }
}
