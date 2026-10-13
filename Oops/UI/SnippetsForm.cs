using System.Drawing;
using System.Windows.Forms;
using Oops.Core;
using Oops.Settings;

namespace Oops.UI;

/// <summary>
/// Редактор сниппетов: сокращение слева, текст справа, по строке на сниппет.
///
/// Правится копия: список в настройках меняется только по «Сохранить» и только
/// если всё прошло проверку. Ошибки — пробел в сокращении, сокращение без
/// текста, два сокращения на одни и те же клавиши — не пропускаются молча:
/// такой сниппет просто не сработал бы, и человек не узнал бы почему.
/// </summary>
internal sealed class SnippetsForm : ThemedForm
{
    private static readonly int ContentWidth = Theme.Px(620);
    private static readonly int CardInnerWidth = ContentWidth - Theme.S3 * 2;
    private static readonly int TriggerWidth = Theme.Px(140);
    private static readonly int RemoveWidth = Theme.Px(36);

    private readonly AppSettings _settings;
    private readonly FlowLayoutPanel _list = new();

    private SnippetsForm(AppSettings settings)
    {
        _settings = settings;

        Text = L10n.T("snippets.window.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        var save = new FlatButton
        {
            Text = L10n.T("common.save"),
            Primary = true,
            AutoSize = true,
            MinimumSize = new Size(Theme.Px(140), 0),
        };
        save.Click += (_, _) => TrySave();
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

        Add(root, new Label
        {
            Text = L10n.T("snippets.hint"),
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(Theme.S1, 0, 0, Theme.S3),
            BackColor = Color.Transparent,
        });
        Add(root, ListCard());
        Add(root, ButtonBar.Create(ContentWidth, new Padding(0, Theme.S4, 0, 0), save, cancel));

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AcceptButton = null;            // Enter в поле — не «сохранить и закрыть»
        CancelButton = cancel;

        foreach (var snippet in settings.Snippets) AddRow(snippet.Trigger, snippet.Text);
        if (settings.Snippets.Count == 0) AddRow(string.Empty, string.Empty);
    }

    /// <summary>Показывает редактор. true — список сохранён в настройки (но ещё не на диск).</summary>
    public static bool Edit(AppSettings settings)
    {
        using var form = new SnippetsForm(settings);
        return form.ShowDialog() == DialogResult.OK;
    }

    // -------------------------------------------------------------- вёрстка

    private Control ListCard()
    {
        var card = NewCard(out var rows);

        var header = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, Theme.S1),
        };
        header.Controls.Add(Caption(L10n.T("snippets.col.trigger"), TriggerWidth + Theme.S2));
        header.Controls.Add(Caption(L10n.T("snippets.col.text"), TextWidth()));
        Add(rows, header);

        // Высота списка постоянная: окно, растущее с каждым сниппетом, уехало
        // бы за край экрана, а прыгающее при добавлении строки — раздражает.
        _list.FlowDirection = FlowDirection.TopDown;
        _list.WrapContents = false;
        _list.AutoScroll = true;
        _list.BackColor = Color.Transparent;
        _list.Margin = new Padding(0);
        _list.Size = new Size(CardInnerWidth, Theme.Px(260));
        _list.HandleCreated += (_, _) => Theme.ApplyScrollbarChrome(_list);
        Add(rows, _list);

        var add = new FlatButton
        {
            Text = L10n.T("snippets.add"),
            AutoSize = true,
            Margin = new Padding(0, Theme.S2, 0, 0),
        };
        add.Click += (_, _) =>
        {
            var trigger = AddRow(string.Empty, string.Empty);
            _list.ScrollControlIntoView(trigger.Parent!);
            trigger.Focus();
        };
        Add(rows, add);

        return card;
    }

    /// <summary>Ширина поля текста: остаток строки за вычетом полосы прокрутки.</summary>
    private static int TextWidth()
        => CardInnerWidth - TriggerWidth - RemoveWidth - Theme.S2 * 2
           - SystemInformation.VerticalScrollBarWidth - Theme.S1;

    private static Label Caption(string text, int width) => new()
    {
        Text = text,
        Font = Theme.Caption,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        MinimumSize = new Size(width, 0),
        MaximumSize = new Size(width, 0),
        Margin = new Padding(0),
        BackColor = Color.Transparent,
    };

    private TextBox AddRow(string trigger, string text)
    {
        var triggerBox = Field(trigger, TriggerWidth);
        var textBox = Field(text, TextWidth());
        var remove = new FlatButton
        {
            Text = "✕",
            AutoSize = false,
            Size = new Size(RemoveWidth, triggerBox.PreferredHeight),
            Margin = new Padding(0),
            TabStop = false,
        };

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, Theme.S1),
        };
        triggerBox.Margin = new Padding(0, 0, Theme.S2, 0);
        textBox.Margin = new Padding(0, 0, Theme.S2, 0);
        row.Controls.Add(triggerBox);
        row.Controls.Add(textBox);
        row.Controls.Add(remove);
        remove.Click += (_, _) =>
        {
            _list.Controls.Remove(row);
            row.Dispose();
        };

        _list.Controls.Add(row);
        return triggerBox;
    }

    private static TextBox Field(string value, int width) => new()
    {
        Text = value,
        Width = width,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        Font = Theme.Mono,              // в сокращении важен каждый символ
    };

    // --------------------------------------------------------------- логика

    private void TrySave()
    {
        var snippets = new List<Snippet>();
        var seen = new Dictionary<string, string>();

        foreach (Control row in _list.Controls)
        {
            var trigger = row.Controls[0].Text.Trim();
            var text = row.Controls[1].Text;
            if (trigger.Length == 0 && text.Length == 0) continue;   // пустая строка — не ошибка

            string? problem = null;
            if (trigger.Length == 0) problem = L10n.T("snippets.bad.noTrigger", text);
            else if (!SnippetMatcher.IsValidTrigger(trigger)) problem = L10n.T("snippets.bad.space", trigger);
            else if (text.Length == 0) problem = L10n.T("snippets.bad.empty", trigger);
            else
            {
                var key = SnippetMatcher.Key(trigger, LayoutConverter.Pair);
                if (seen.TryGetValue(key, out var earlier))
                    problem = L10n.T("snippets.bad.duplicate", earlier, trigger);
                else seen[key] = trigger;
            }

            if (problem != null)
            {
                Notice.Warn(this, L10n.T("snippets.bad.title"), problem, L10n.T("snippets.bad.hint"));
                row.Controls[trigger.Length == 0 ? 0 : 1].Focus();
                return;
            }
            snippets.Add(new Snippet { Trigger = trigger, Text = text });
        }

        _settings.Snippets = snippets;
        DialogResult = DialogResult.OK;
    }

    // ------------------------------------------------------------- мелочёвка

    private static Card NewCard(out TableLayoutPanel rows)
    {
        // Ширина — сразу: до раскладки карточка думает, что она 200 px (см. CLAUDE.md).
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
