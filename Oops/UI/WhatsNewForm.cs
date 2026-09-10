using System.Drawing;
using System.Windows.Forms;
using Oops.Core;
using Oops.Settings;

namespace Oops.UI;

/// <summary>
/// «Что нового» — после обновления и по требованию из меню трея.
///
/// Программа живёт в трее и обновляется молча: без такого окна человек узнаёт
/// о новой функции только случайно, а хоткей, о котором он не знает, ничем не
/// отличается от отсутствующего.
///
/// Поэтому каждый пункт отвечает не «что сделано», а «как этим пользоваться»,
/// и рядом стоит ФАКТИЧЕСКОЕ сочетание из настроек — человек мог поменять его,
/// и показывать ему чужое было бы худшим способом объяснить функцию.
/// </summary>
internal sealed class WhatsNewForm : ThemedForm
{
    private static readonly int ContentWidth = Theme.Px(620);
    private static readonly int CardInnerWidth = ContentWidth - Theme.S3 * 2;

    private readonly AppSettings _settings;
    private readonly Panel _scroller;
    private readonly TableLayoutPanel _stack;

    private WhatsNewForm(AppSettings settings, IReadOnlyList<ChangeSet> sets, bool afterUpdate)
    {
        _settings = settings;

        Text = L10n.T("news.window.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;      // окно самостоятельное, не диалог поверх другого
        StartPosition = FormStartPosition.CenterScreen;

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
            Text = afterUpdate
                ? L10n.T("news.header.updated", UpdateService.CurrentVersion.ToString(3))
                : L10n.T("news.header.plain"),
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });

        Add(root, new Label
        {
            Text = L10n.T("news.header.hint"),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S2),
            BackColor = Color.Transparent,
        });

        // Список версий — в прокручиваемой панели: «что нового» за несколько
        // версий не обязано помещаться на экран, а окно выше экрана хуже
        // прокрутки во всех отношениях.
        var scroller = new Panel
        {
            AutoScroll = true,
            BackColor = Theme.Canvas,
            Width = ContentWidth,
            Margin = new Padding(0),
        };
        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Margin = new Padding(0),
            // Минус ширина полосы прокрутки: иначе содержимое уезжает под неё
            // и появляется ещё и горизонтальная.
            Width = ContentWidth - SystemInformation.VerticalScrollBarWidth,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Поля заполняем ДО сборки карточек: они считают свою ширину от _stack,
        // а к моменту цикла он должен быть уже на месте.
        _scroller = scroller;
        _stack = stack;

        foreach (var set in sets)
        {
            Add(stack, Section("oops " + set.Version.ToString(3)));
            Add(stack, VersionCard(set));
        }
        scroller.Controls.Add(stack);
        Add(root, scroller);

        var ok = new FlatButton
        {
            Text = L10n.T("notice.ok"),
            Primary = true,
            AutoSize = true,
            MinimumSize = new Size(Theme.Px(120), 0),
            DialogResult = DialogResult.OK,
        };
        Add(root, ButtonBar.Create(ContentWidth, new Padding(0, Theme.S4, 0, 0), ok));

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AcceptButton = ok;
        CancelButton = ok;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Высоту прокручиваемой области считаем ПОСЛЕ раскладки: до неё
        // переносы строк не известны и любое число здесь — выдумка
        // (см. CLAUDE.md про FitPages).
        _stack.PerformLayout();
        int wanted = _stack.PreferredSize.Height;

        int chrome = Height - ClientSize.Height;
        int aroundScroller = ClientSize.Height - _scroller.Height;
        int maxScroller = Screen.FromControl(this).WorkingArea.Height - chrome - aroundScroller;

        AutoSize = false;
        _scroller.Height = Math.Min(wanted, Math.Max(Theme.Px(200), maxScroller));
        ClientSize = new Size(ClientSize.Width, aroundScroller + _scroller.Height);
        CenterOnWorkArea();
    }

    // ------------------------------------------------------------------ API

    /// <summary>Показывает окно целиком, по всем версиям — из меню трея.</summary>
    public static void ShowAll(AppSettings settings)
    {
        using var form = new WhatsNewForm(settings, Changelog.All, afterUpdate: false);
        form.ShowDialog();
    }

    /// <summary>
    /// Показывает окно, если программу обновили с прошлого запуска, и
    /// запоминает текущую версию.
    ///
    /// На свежей установке НЕ показывает: там своё дело делает мастер первого
    /// запуска, а вывалить на нового человека историю версий — верный способ
    /// заставить его закрыть оба окна не читая.
    /// </summary>
    public static void ShowIfUpdated(AppSettings settings)
    {
        var current = UpdateService.CurrentVersion;
        var seen = Version.TryParse(settings.LastSeenVersion, out var v) ? v : null;

        // Версию запоминаем в любом случае — иначе окно всплывало бы при
        // каждом запуске, пока настройки не сохранят по другому поводу.
        settings.LastSeenVersion = current.ToString();
        settings.Save();

        if (seen == null) return;                       // первая установка
        var sets = Changelog.Since(seen);
        if (sets.Count == 0) return;

        using var form = new WhatsNewForm(settings, sets, afterUpdate: true);
        form.ShowDialog();
    }

    // -------------------------------------------------------------- вёрстка

    private Control VersionCard(ChangeSet set)
    {
        var card = new Card { Margin = new Padding(0), Width = _stack.Width };
        var rows = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Dock = DockStyle.Top,
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        card.Controls.Add(rows);
        rows.SizeChanged += (_, _) => card.Height = rows.Height + card.Padding.Vertical;
        card.Height = rows.Height + card.Padding.Vertical;

        bool first = true;
        foreach (var entry in set.Entries)
        {
            if (!first) Add(rows, Divider());
            first = false;
            Add(rows, Entry(entry));
        }
        return card;
    }

    private Control Entry(ChangeEntry entry)
    {
        int inner = _stack.Width - Theme.S3 * 2;

        var rows = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, Theme.S2, 0, Theme.S2),
            Width = inner,
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        Add(rows, new Label
        {
            Text = L10n.T(entry.TitleKey),
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });

        var combo = Combo(entry.Hotkey);
        if (combo != null)
        {
            var keys = new HotkeyDisplay
            {
                Size = new Size(Theme.Px(220), Theme.KeyRowHeight),
                Margin = new Padding(0, 0, 0, Theme.S1),
            };
            keys.SetCombo(combo);
            Add(rows, keys);
        }

        Add(rows, new Label
        {
            Text = L10n.T(entry.BodyKey),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        });
        return rows;
    }

    /// <summary>Фактическое сочетание из настроек, а не умолчание.</summary>
    private string? Combo(HotkeyRef which) => which switch
    {
        HotkeyRef.Convert => _settings.ConvertHotkey.ToString(),
        HotkeyRef.Case => _settings.ChangeCaseHotkey.ToString(),
        HotkeyRef.Translate => _settings.TranslateHotkey.ToString(),
        HotkeyRef.Voice => _settings.VoiceHotkey.ToString(),
        HotkeyRef.Replace => _settings.ReplaceHotkey.ToString(),
        _ => null,
    };

    private static Control Section(string text) => new Label
    {
        Text = text,
        Font = Theme.SectionLabel,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        Margin = new Padding(Theme.S1, Theme.S3, 0, Theme.S2),
        BackColor = Color.Transparent,
    };

    private static Control Divider() => new Panel
    {
        Height = 1,
        BackColor = Theme.Border,
        Margin = new Padding(0),
    };

    private static void Add(TableLayoutPanel host, Control child)
    {
        if (child.Anchor == (AnchorStyles.Top | AnchorStyles.Left))
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }
}
